using System;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;

namespace Artel.Streaming
{
    /// <summary>
    /// The `type` discriminators from docs/streaming-protocol.md. Named here so a change to the
    /// contract is a compile-time search rather than a hunt through string literals.
    /// </summary>
    internal static class StreamMessageType
    {
        public const string StreamStart = "STREAM_START";
        public const string StreamRenew = "STREAM_RENEW";
        public const string StreamStop = "STREAM_STOP";
        public const string StreamState = "STREAM_STATE";
        public const string WebRtcOffer = "WEBRTC_OFFER";
        public const string WebRtcAnswer = "WEBRTC_ANSWER";
        public const string WebRtcIce = "WEBRTC_ICE";
    }

    internal static class StreamState
    {
        public const string Connecting = "CONNECTING";
        public const string Live = "LIVE";
        public const string Failed = "FAILED";
        public const string Stopped = "STOPPED";
    }

    /// <summary>
    /// Holds at most one <see cref="IArtelStreamSession"/> and is the only place that emits
    /// STREAM_STATE.
    /// </summary>
    internal sealed class ArtelStreamHost
    {
        private readonly IJsonCodec jsonCodec;
        private readonly IStreamSignalSender signals;
        private readonly IArtelStreamSessionFactory sessionFactory;
        private IArtelStreamSession session;

        public ArtelStreamHost(
            IJsonCodec jsonCodec,
            IStreamSignalSender signals,
            IArtelStreamSessionFactory sessionFactory)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
            this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        public bool HasLiveSession { get { return session != null; } }

        /// <summary>
        /// Returns false for anything that is not streaming signalling, so the caller's existing
        /// message routing is left to handle it.
        /// </summary>
        public bool TryHandleMessage(string type, string json, float currentTime)
        {
            switch (type)
            {
                case StreamMessageType.StreamStart:
                    StartSession(jsonCodec.Deserialize<StreamStartMessageDto>(json), currentTime);
                    return true;

                case StreamMessageType.StreamRenew:
                    RenewSession(jsonCodec.Deserialize<StreamRenewMessageDto>(json), currentTime);
                    return true;

                case StreamMessageType.StreamStop:
                    StopSession(jsonCodec.Deserialize<StreamStopMessageDto>(json));
                    return true;

                case StreamMessageType.WebRtcAnswer:
                    AcceptAnswer(jsonCodec.Deserialize<WebRtcAnswerMessageDto>(json));
                    return true;

                case StreamMessageType.WebRtcIce:
                    AcceptIceCandidate(jsonCodec.Deserialize<WebRtcIceMessageDto>(json));
                    return true;

                default:
                    return false;
            }
        }

        public void Tick(float currentTime)
        {
            if (session == null || !session.HasExpired(currentTime))
            {
                return;
            }

            // Lease expiry is the ordinary end of a stream, not a fault: the cases it exists for —
            // a closed lid, a killed browser, a dead server — never send STREAM_STOP.
            Debug.Log("[Artel] Stream lease expired, tearing the peer connection down.");
            EndSession(StreamState.Stopped, null);
        }

        public void Stop()
        {
            EndSession(StreamState.Stopped, null);
        }

        private void StartSession(StreamStartMessageDto start, float currentTime)
        {
            if (start == null || string.IsNullOrEmpty(start.StreamId))
            {
                return;
            }

            if (start.LeaseSeconds <= 0)
            {
                // A non-positive lease would expire on the first tick, which looks like a stream
                // that connects and immediately dies rather than like a malformed message.
                Debug.LogWarning("[Artel] STREAM_START carried no usable lease, ignoring it.");
                return;
            }

            // STREAM_START arriving while a session is live replaces it. The server sends no
            // preceding STREAM_STOP, so replacement must never be an assumption about two
            // messages arriving in order.
            EndSession(StreamState.Stopped, null);

            session = sessionFactory.Create(start);
            session.StateChanged += OnSessionStateChanged;
            session.Start(currentTime);
        }

        private void RenewSession(StreamRenewMessageDto renew, float currentTime)
        {
            if (renew == null || !BelongsToCurrentSession(renew.StreamId))
            {
                return;
            }

            session.Renew(currentTime);
        }

        private void StopSession(StreamStopMessageDto stop)
        {
            if (stop == null || !BelongsToCurrentSession(stop.StreamId))
            {
                return;
            }

            EndSession(StreamState.Stopped, null);
        }

        private void AcceptAnswer(WebRtcAnswerMessageDto answer)
        {
            if (answer == null || !BelongsToCurrentSession(answer.StreamId))
            {
                return;
            }

            session.AcceptAnswer(answer.Sdp);
        }

        private void AcceptIceCandidate(WebRtcIceMessageDto ice)
        {
            if (ice == null || !BelongsToCurrentSession(ice.StreamId))
            {
                return;
            }

            session.AcceptIceCandidate(ice.Candidate);
        }

        /// <summary>
        /// Signalling for a session that has already ended is dropped. Under the takeover policy
        /// the displaced viewer's last ICE candidates are still in flight while the replacement is
        /// negotiating, so this overlap is the normal case rather than a rare one, and applying
        /// one of those candidates corrupts the new negotiation.
        /// </summary>
        private bool BelongsToCurrentSession(string streamId)
        {
            return session != null && session.StreamId == streamId;
        }

        private void OnSessionStateChanged(string state, string error)
        {
            if (session == null)
            {
                return;
            }

            ReportState(session.StreamId, state, error);

            if (state != StreamState.Failed)
            {
                return;
            }

            // A failed peer still holds a RenderTexture and a capture loop, and nothing else will
            // come to stop it: the viewer keeps renewing a lease against a peer carrying no media.
            DisposeSession();
        }

        private void EndSession(string state, string error)
        {
            if (session == null)
            {
                return;
            }

            ReportState(session.StreamId, state, error);
            DisposeSession();
        }

        private void DisposeSession()
        {
            if (session == null)
            {
                return;
            }

            session.StateChanged -= OnSessionStateChanged;
            session.Stop();
            session = null;
        }

        private void ReportState(string streamId, string state, string error)
        {
            signals.Send(new StreamStateMessageDto
            {
                Type = StreamMessageType.StreamState,
                StreamId = streamId,
                State = state,
                Error = error
            });
        }
    }
}
