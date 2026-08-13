using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Artel.Streaming;
using NUnit.Framework;

namespace Artel.Tests.Streaming
{
    /// <summary>
    /// Covers the signalling state machine. The peer connection and the encoder are not reachable
    /// from edit mode, so the session is faked and the media is deliberately out of scope.
    /// </summary>
    public sealed class ArtelStreamHostTests
    {
        private const string FirstStreamId = "stream-1";
        private const string SecondStreamId = "stream-2";
        private const long LeaseSeconds = 15;

        /// <summary>A plausible frame. The lease is spent frame by frame, so time has to be
        /// advanced the way the frame loop advances it.</summary>
        private const float FrameSeconds = 0.25f;

        private RecordingSignalSender signals;
        private FakeStreamSessionFactory sessions;
        private ArtelStreamHost host;

        [SetUp]
        public void SetUp()
        {
            signals = new RecordingSignalSender();
            sessions = new FakeStreamSessionFactory();
            host = new ArtelStreamHost(new NewtonsoftJsonCodec(), signals, sessions);
        }

        [Test]
        public void TryHandleMessage_LeavesUnrelatedMessagesToTheCaller()
        {
            Assert.That(host.TryHandleMessage("GAME_STATE", "{\"type\":\"GAME_STATE\"}", 0f), Is.False);
            Assert.That(host.TryHandleMessage("ACTION", "{\"type\":\"ACTION\"}", 0f), Is.False);
            Assert.That(sessions.Created, Is.Empty);
        }

        [Test]
        public void Tick_TearsDownTheSessionWhenTheLeaseExpires()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            TickFrames(0f, LeaseSeconds - 1f);

            Assert.That(host.HasLiveSession, Is.True);
            Assert.That(sessions.Created[0].IsStopped, Is.False);

            TickFrames(LeaseSeconds - 1f, LeaseSeconds + 1f);

            Assert.That(host.HasLiveSession, Is.False);
            Assert.That(sessions.Created[0].IsStopped, Is.True);
            Assert.That(signals.LastState(), Is.EqualTo(StreamState.Stopped));
        }

        [Test]
        public void Renew_PushesTheLeaseDeadlineOut()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            TickFrames(0f, 10f);
            host.TryHandleMessage(StreamMessageType.StreamRenew, StreamRenewJson(FirstStreamId), 10f);
            TickFrames(10f, LeaseSeconds);

            Assert.That(host.HasLiveSession, Is.True);

            TickFrames(LeaseSeconds, 10f + LeaseSeconds + 1f);

            Assert.That(host.HasLiveSession, Is.False);
        }

        /// <summary>
        /// The frame that resumes a suspended app carries the whole away period in its delta. That
        /// is not the viewer having gone quiet — nothing could have been read while the process was
        /// frozen — so it must not cost the session.
        /// </summary>
        [Test]
        public void Tick_SurvivesTheFrameThatResumesFromASuspendedApp()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            TickFrames(0f, 0.5f);
            host.Tick(600.5f);

            Assert.That(host.HasLiveSession, Is.True);
            Assert.That(sessions.Created[0].IsStopped, Is.False);
        }

        /// <summary>
        /// The other half of the same rule, and the one that keeps this a dead-man timer: coming
        /// back does not buy a session that nobody renews.
        /// </summary>
        [Test]
        public void Tick_StillExpiresAfterAResumeWhenNoRenewalFollows()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            TickFrames(0f, 0.5f);
            host.Tick(600.5f);
            TickFrames(600.5f, 600.5f + LeaseSeconds);

            Assert.That(host.HasLiveSession, Is.False);
            Assert.That(sessions.Created[0].IsStopped, Is.True);
            Assert.That(signals.LastState(), Is.EqualTo(StreamState.Stopped));
        }

        /// <summary>
        /// A frame over the cap is charged the cap, not excused. Without that, a game throttled
        /// below one frame per second would hold a lease forever and go on encoding for a viewer
        /// who left.
        /// </summary>
        [Test]
        public void Tick_ChargesAnOverlongFrameTheCapRatherThanExcusingIt()
        {
            const float SlowFrameSeconds = 5f;

            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            // Ten frames spanning fifty seconds of wall clock: far past the lease in real time,
            // but only ten seconds of it are chargeable, so the session is still owed five.
            for (var time = SlowFrameSeconds; time <= 10f * SlowFrameSeconds; time += SlowFrameSeconds)
            {
                host.Tick(time);
            }

            Assert.That(host.HasLiveSession, Is.True);

            for (var time = 11f * SlowFrameSeconds;
                 time <= (LeaseSeconds + 1f) * SlowFrameSeconds;
                 time += SlowFrameSeconds)
            {
                host.Tick(time);
            }

            Assert.That(host.HasLiveSession, Is.False);
            Assert.That(signals.LastState(), Is.EqualTo(StreamState.Stopped));
        }

        [Test]
        public void Renew_AfterAResumeKeepsTheSessionAlive()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            TickFrames(0f, 0.5f);
            host.Tick(600.5f);
            host.TryHandleMessage(StreamMessageType.StreamRenew, StreamRenewJson(FirstStreamId), 600.5f);
            TickFrames(600.5f, 600.5f + LeaseSeconds - 1f);

            Assert.That(host.HasLiveSession, Is.True);
        }

        [Test]
        public void StreamStart_ReplacesALiveSessionWithoutAPrecedingStop()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(SecondStreamId), 1f);

            Assert.That(sessions.Created.Count, Is.EqualTo(2));
            Assert.That(sessions.Created[0].IsStopped, Is.True);
            Assert.That(sessions.Created[1].IsStopped, Is.False);
            Assert.That(sessions.Created[1].IsStarted, Is.True);
            Assert.That(host.HasLiveSession, Is.True);

            var displacedStop = signals.States.Find(
                state => state.StreamId == FirstStreamId && state.State == StreamState.Stopped);
            Assert.That(displacedStop, Is.Not.Null);
        }

        [Test]
        public void Signalling_ForAReplacedSessionIsDropped()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(SecondStreamId), 1f);

            var current = sessions.Created[1];

            host.TryHandleMessage(StreamMessageType.WebRtcAnswer, AnswerJson(FirstStreamId), 2f);
            host.TryHandleMessage(StreamMessageType.WebRtcIce, IceJson(FirstStreamId), 2f);
            host.TryHandleMessage(StreamMessageType.StreamStop, StreamStopJson(FirstStreamId), 2f);

            Assert.That(current.Answers, Is.Empty);
            Assert.That(current.Candidates, Is.Empty);
            Assert.That(host.HasLiveSession, Is.True);

            host.TryHandleMessage(StreamMessageType.WebRtcAnswer, AnswerJson(SecondStreamId), 3f);
            host.TryHandleMessage(StreamMessageType.WebRtcIce, IceJson(SecondStreamId), 3f);

            Assert.That(current.Answers, Is.EqualTo(new[] { "v=0" }));
            Assert.That(current.Candidates.Count, Is.EqualTo(1));
            Assert.That(current.Candidates[0].Candidate, Is.EqualTo("candidate:1 1 udp 1 10.0.0.1 5000 typ host"));
            Assert.That(current.Candidates[0].SdpMid, Is.EqualTo("0"));
            Assert.That(current.Candidates[0].SdpMLineIndex, Is.EqualTo(0));
        }

        [Test]
        public void FailedSession_IsReportedAndReleased()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            sessions.Created[0].RaiseState(StreamState.Failed, "ICE connection failed.");

            Assert.That(signals.LastState(), Is.EqualTo(StreamState.Failed));
            Assert.That(sessions.Created[0].IsStopped, Is.True);
            Assert.That(host.HasLiveSession, Is.False);
        }

        [Test]
        public void StreamStart_WithoutAUsableLeaseIsIgnored()
        {
            const string json =
                "{\"type\":\"STREAM_START\",\"streamId\":\"stream-1\",\"iceServers\":[]," +
                "\"video\":{\"maxWidth\":1280,\"maxFramerate\":30},\"leaseSeconds\":0}";

            host.TryHandleMessage(StreamMessageType.StreamStart, json, 0f);

            Assert.That(sessions.Created, Is.Empty);
            Assert.That(host.HasLiveSession, Is.False);
        }

        [Test]
        public void StreamStart_CarriesTheServersIceConfigurationThrough()
        {
            host.TryHandleMessage(StreamMessageType.StreamStart, StreamStartJson(FirstStreamId), 0f);

            var start = sessions.Created[0].StartMessage;

            Assert.That(start.IceServers.Count, Is.EqualTo(1));
            Assert.That(start.IceServers[0].Urls, Is.EqualTo(new[] { "stun:stun.example.org:19302" }));
            Assert.That(start.Video.MaxWidth, Is.EqualTo(1280));
            Assert.That(start.Video.MaxFramerate, Is.EqualTo(30));
            Assert.That(start.LeaseSeconds, Is.EqualTo(LeaseSeconds));
        }

        /// <summary>
        /// Ticks the host the way the frame loop does — one call per frame, carrying the absolute
        /// time of that frame — from just after <paramref name="fromSeconds"/> through
        /// <paramref name="toSeconds"/>.
        /// </summary>
        private void TickFrames(float fromSeconds, float toSeconds)
        {
            for (var time = fromSeconds + FrameSeconds; time <= toSeconds; time += FrameSeconds)
            {
                host.Tick(time);
            }
        }

        private static string StreamStartJson(string streamId)
        {
            return "{\"type\":\"STREAM_START\",\"streamId\":\"" + streamId + "\"," +
                   "\"iceServers\":[{\"urls\":[\"stun:stun.example.org:19302\"]," +
                   "\"username\":null,\"credential\":null}]," +
                   "\"video\":{\"maxWidth\":1280,\"maxFramerate\":30}," +
                   "\"leaseSeconds\":" + LeaseSeconds + "}";
        }

        private static string StreamRenewJson(string streamId)
        {
            return "{\"type\":\"STREAM_RENEW\",\"streamId\":\"" + streamId + "\"}";
        }

        private static string StreamStopJson(string streamId)
        {
            return "{\"type\":\"STREAM_STOP\",\"streamId\":\"" + streamId + "\"}";
        }

        private static string AnswerJson(string streamId)
        {
            return "{\"type\":\"WEBRTC_ANSWER\",\"streamId\":\"" + streamId + "\",\"sdp\":\"v=0\"}";
        }

        private static string IceJson(string streamId)
        {
            return "{\"type\":\"WEBRTC_ICE\",\"streamId\":\"" + streamId + "\"," +
                   "\"candidate\":{\"candidate\":\"candidate:1 1 udp 1 10.0.0.1 5000 typ host\"," +
                   "\"sdpMid\":\"0\",\"sdpMLineIndex\":0}}";
        }

        private sealed class RecordingSignalSender : IStreamSignalSender
        {
            public List<StreamStateMessageDto> States { get; } = new List<StreamStateMessageDto>();

            public void Send(object message)
            {
                var state = message as StreamStateMessageDto;
                if (state != null)
                {
                    States.Add(state);
                }
            }

            public string LastState()
            {
                return States.Count == 0 ? null : States[States.Count - 1].State;
            }
        }

        private sealed class FakeStreamSessionFactory : IArtelStreamSessionFactory
        {
            public List<FakeStreamSession> Created { get; } = new List<FakeStreamSession>();

            public IArtelStreamSession Create(StreamStartMessageDto start)
            {
                var session = new FakeStreamSession(start);
                Created.Add(session);
                return session;
            }
        }

        /// <summary>
        /// Keeps the real <see cref="StreamLease"/> so the timer under test is the shipped one.
        /// Everything below it is WebRTC and is not reachable from edit mode.
        /// </summary>
        private sealed class FakeStreamSession : IArtelStreamSession
        {
            private readonly StreamLease lease;

            public FakeStreamSession(StreamStartMessageDto start)
            {
                StartMessage = start;
                lease = new StreamLease(start.LeaseSeconds);
            }

            public StreamStartMessageDto StartMessage { get; private set; }
            public string StreamId { get { return StartMessage.StreamId; } }
            public bool IsStarted { get; private set; }
            public bool IsStopped { get; private set; }
            public List<string> Answers { get; } = new List<string>();
            public List<WebRtcIceCandidateDto> Candidates { get; } = new List<WebRtcIceCandidateDto>();

            public event Action<string, string> StateChanged;

            public void Start(float currentTime)
            {
                IsStarted = true;
                lease.Renew(currentTime);
                RaiseState(StreamState.Connecting, null);
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
                Answers.Add(sdp);
            }

            public void AcceptIceCandidate(WebRtcIceCandidateDto candidate)
            {
                Candidates.Add(candidate);
            }

            public void Stop()
            {
                IsStopped = true;
            }

            public void RaiseState(string state, string error)
            {
                var handler = StateChanged;
                if (handler != null)
                {
                    handler(state, error);
                }
            }
        }
    }
}
