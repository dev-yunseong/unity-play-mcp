using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Unity.WebRTC;
using UnityEngine;

namespace Artel.Streaming
{
    internal interface IArtelStreamSession
    {
        string StreamId { get; }

        /// <summary>
        /// Raised with a <see cref="StreamState"/> value and an optional error. The host, not the
        /// session, puts it on the wire — one emitter is what keeps a torn-down session from
        /// reporting state after its replacement is already live.
        /// </summary>
        event Action<string, string> StateChanged;

        void Start(float currentTime);
        void Renew(float currentTime);

        /// <summary>
        /// Belongs to the frame tick and nowhere else: it spends the lease by the time elapsed
        /// since the last call, so asking a second time in one frame charges the session twice.
        /// </summary>
        bool HasExpired(float currentTime);
        void AcceptAnswer(string sdp);
        void AcceptIceCandidate(WebRtcIceCandidateDto candidate);
        void Stop();
    }

    internal interface IArtelStreamSessionFactory
    {
        IArtelStreamSession Create(StreamStartMessageDto start);
    }

    /// <summary>
    /// One peer connection and everything whose lifetime is tied to it.
    ///
    /// The <see cref="ScreenVideoSource"/> is owned outright rather than shared: capture starts
    /// with the session and is released with it, so there is no state in which the game is paying
    /// to encode frames with no peer attached. That is what keeps a build that never streams free.
    /// </summary>
    internal sealed class ArtelStreamSession : IArtelStreamSession
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly IStreamSignalSender signals;
        private readonly ScreenVideoSource videoSource;
        private readonly StreamLease lease;
        private readonly RTCConfiguration configuration;
        private RTCPeerConnection peer;
        private VideoStreamTrack videoTrack;
        private bool stopped;

        public ArtelStreamSession(
            MonoBehaviour coroutineHost,
            IStreamSignalSender signals,
            StreamStartMessageDto start)
        {
            this.coroutineHost = coroutineHost != null
                ? coroutineHost
                : throw new ArgumentNullException(nameof(coroutineHost));
            this.signals = signals ?? throw new ArgumentNullException(nameof(signals));

            if (start == null)
            {
                throw new ArgumentNullException(nameof(start));
            }

            StreamId = start.StreamId;
            configuration = BuildConfiguration(start.IceServers);
            lease = new StreamLease(start.LeaseSeconds);
            videoSource = new ScreenVideoSource(
                new BehaviourCaptureLoopRunner(coroutineHost),
                start.Video != null ? start.Video.MaxWidth : 0,
                start.Video != null ? start.Video.MaxFramerate : 0);
        }

        public string StreamId { get; private set; }

        public event Action<string, string> StateChanged;

        public void Start(float currentTime)
        {
            lease.Renew(currentTime);

            var localConfiguration = configuration;
            peer = new RTCPeerConnection(ref localConfiguration);
            peer.OnIceCandidate = EmitIceCandidate;
            peer.OnIceConnectionChange = OnIceConnectionChange;

            videoTrack = new VideoStreamTrack(videoSource.Start());
            peer.AddTrack(videoTrack);

            Report(StreamState.Connecting, null);
            coroutineHost.StartCoroutine(Negotiate());
        }

        public void Renew(float currentTime)
        {
            lease.Renew(currentTime);
        }

        public bool HasExpired(float currentTime)
        {
            return lease.HasExpired(currentTime);
        }

        public void AcceptAnswer(string sdp)
        {
            if (stopped || peer == null || string.IsNullOrEmpty(sdp))
            {
                return;
            }

            coroutineHost.StartCoroutine(ApplyAnswer(sdp));
        }

        public void AcceptIceCandidate(WebRtcIceCandidateDto candidate)
        {
            if (stopped || peer == null || candidate == null || string.IsNullOrEmpty(candidate.Candidate))
            {
                return;
            }

            peer.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex
            }));
        }

        public void Stop()
        {
            if (stopped)
            {
                return;
            }

            // Set before anything is released: the negotiation coroutines resume on their own
            // schedule and this is what stops them touching a disposed peer.
            stopped = true;

            // Capture first, so no blit lands on a texture the track is still reading from.
            videoSource.Stop();

            if (videoTrack != null)
            {
                videoTrack.Dispose();
                videoTrack = null;
            }

            if (peer == null)
            {
                return;
            }

            peer.OnIceCandidate = null;
            peer.OnIceConnectionChange = null;
            peer.Close();
            peer.Dispose();
            peer = null;
        }

        private IEnumerator Negotiate()
        {
            var offer = peer.CreateOffer();
            yield return offer;

            if (stopped)
            {
                yield break;
            }

            if (offer.IsError)
            {
                Report(StreamState.Failed, "Offer creation failed: " + offer.Error.message);
                yield break;
            }

            var description = offer.Desc;
            var localDescription = peer.SetLocalDescription(ref description);
            yield return localDescription;

            if (stopped)
            {
                yield break;
            }

            if (localDescription.IsError)
            {
                Report(StreamState.Failed, "Local description rejected: " + localDescription.Error.message);
                yield break;
            }

            signals.Send(new WebRtcOfferMessageDto
            {
                Type = StreamMessageType.WebRtcOffer,
                StreamId = StreamId,
                Sdp = description.sdp
            });
        }

        private IEnumerator ApplyAnswer(string sdp)
        {
            var description = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
            var remoteDescription = peer.SetRemoteDescription(ref description);
            yield return remoteDescription;

            if (stopped || !remoteDescription.IsError)
            {
                yield break;
            }

            Report(StreamState.Failed, "Answer rejected: " + remoteDescription.Error.message);
        }

        private void EmitIceCandidate(RTCIceCandidate candidate)
        {
            if (stopped || candidate == null)
            {
                return;
            }

            signals.Send(new WebRtcIceMessageDto
            {
                Type = StreamMessageType.WebRtcIce,
                StreamId = StreamId,
                Candidate = new WebRtcIceCandidateDto
                {
                    Candidate = candidate.Candidate,
                    SdpMid = candidate.SdpMid,
                    SdpMLineIndex = candidate.SdpMLineIndex
                }
            });
        }

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            if (stopped)
            {
                return;
            }

            switch (state)
            {
                case RTCIceConnectionState.Connected:
                case RTCIceConnectionState.Completed:
                    Report(StreamState.Live, null);
                    return;

                case RTCIceConnectionState.Failed:
                    // Reported rather than left to time out. Without TURN, a game and a browser on
                    // different networks negotiate successfully and then carry no media; as an
                    // endless "connecting" that reads as a hang instead of a network limit.
                    Report(StreamState.Failed, "ICE connection failed.");
                    return;

                default:
                    // Disconnected is recoverable on its own and Closed follows our own teardown,
                    // so neither is worth reporting as a state change.
                    return;
            }
        }

        private void Report(string state, string error)
        {
            var handler = StateChanged;
            if (handler != null)
            {
                handler(state, error);
            }
        }

        /// <summary>
        /// ICE servers come from STREAM_START and are never compiled in. The SDK ships to
        /// customers, so a baked default would have every customer's game contacting a
        /// third-party STUN host.
        /// </summary>
        private static RTCConfiguration BuildConfiguration(List<StreamIceServerDto> iceServers)
        {
            var servers = new List<RTCIceServer>();

            foreach (var server in iceServers ?? new List<StreamIceServerDto>())
            {
                if (server == null || server.Urls == null || server.Urls.Count == 0)
                {
                    continue;
                }

                servers.Add(new RTCIceServer
                {
                    urls = server.Urls.ToArray(),
                    // Empty strings are not the same as absent here: a TURN entry carrying blank
                    // credentials produces candidates that fail every time and only slows ICE down.
                    username = string.IsNullOrEmpty(server.Username) ? null : server.Username,
                    credential = string.IsNullOrEmpty(server.Credential) ? null : server.Credential
                });
            }

            return new RTCConfiguration { iceServers = servers.ToArray() };
        }
    }

    internal sealed class WebRtcStreamSessionFactory : IArtelStreamSessionFactory
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly IStreamSignalSender signals;

        public WebRtcStreamSessionFactory(MonoBehaviour coroutineHost, IStreamSignalSender signals)
        {
            this.coroutineHost = coroutineHost != null
                ? coroutineHost
                : throw new ArgumentNullException(nameof(coroutineHost));
            this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        }

        public IArtelStreamSession Create(StreamStartMessageDto start)
        {
            return new ArtelStreamSession(coroutineHost, signals, start);
        }
    }
}
