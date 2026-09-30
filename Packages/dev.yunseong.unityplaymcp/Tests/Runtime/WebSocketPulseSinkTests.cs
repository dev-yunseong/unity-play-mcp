using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 기존 연결로 나가는 pulse 메시지의 형태를 고정한다.
    /// </summary>
    /// <remarks>
    /// orchestration 은 <c>type</c> 으로 handler 를 고르고 agent 는 나머지를 pulse 문서로 읽는다.
    /// envelope 는 문자열 이어붙이기라 조용히 깨질 수 있어 보낸 내용을 확인한다.
    /// </remarks>
    public sealed class WebSocketPulseSinkTests
    {
        private sealed class FakeTransport : IAgentTransport
        {
            internal readonly List<string> Sent = new List<string>();
            internal bool Connected = true;

            public bool IsConnected { get { return Connected; } }

            public int ClientsOpened { get { return 0; } }

            public void Start() { }

            public void Stop() { }

            public bool TryDequeueMessage(out AgentMessage message)
            {
                message = null;
                return false;
            }

            public void Send(string text) { Sent.Add(text); }

            public void Dispose() { }
        }

        private const string Reading =
            "{\"schema\":2,\"reading\":12,\"frame\":3401,\"scene\":\"TurnBattleScene\"," +
            "\"whole\":true,\"changed\":[]}";

        [Test]
        public void pulse_가_PULSE_프레임으로_나간다()
        {
            var transport = new FakeTransport();
            var sink = new WebSocketPulseSink(() => transport, () => 7L);

            sink.Send(Reading);

            Assert.That(transport.Sent, Has.Count.EqualTo(1));
            Assert.That(
                transport.Sent[0],
                Is.EqualTo(
                    "{\"type\":\"PULSE\",\"id\":7,\"schema\":2,\"reading\":12,\"frame\":3401," +
                    "\"scene\":\"TurnBattleScene\",\"whole\":true,\"changed\":[]}"));
        }

        [Test]
        public void pulse_본문은_한_글자도_바뀌지_않는다()
        {
            var transport = new FakeTransport();
            var sink = new WebSocketPulseSink(() => transport, () => 1L);

            sink.Send(Reading);

            // envelope 두 필드를 걷어내면 넣은 내용이 그대로 남아야 한다.
            var framed = transport.Sent[0];
            var body = "{" + framed.Substring(framed.IndexOf("\"schema\"", StringComparison.Ordinal));
            Assert.That(body, Is.EqualTo(Reading));
        }

        [Test]
        public void pulse_마다_id_가_올라간다()
        {
            var transport = new FakeTransport();
            var next = 0L;
            var sink = new WebSocketPulseSink(() => transport, () => ++next);

            sink.Send(Reading);
            sink.Send(Reading);

            Assert.That(transport.Sent[0], Does.Contain("\"id\":1,"));
            Assert.That(transport.Sent[1], Does.Contain("\"id\":2,"));
        }

        [Test]
        public void 연결이_없으면_던진다()
        {
            // 실패를 삼키면 Pulse 의 손실 복구가 돌지 않아, 읽는 쪽은 다음 whole pulse 까지 틀린 상태로 남는다.
            var sink = new WebSocketPulseSink(() => null, () => 1L);

            Assert.Throws<InvalidOperationException>(() => sink.Send(Reading));
        }

        [Test]
        public void 연결이_끊겨_있으면_던진다()
        {
            var transport = new FakeTransport { Connected = false };
            var sink = new WebSocketPulseSink(() => transport, () => 1L);

            Assert.Throws<InvalidOperationException>(() => sink.Send(Reading));
            Assert.That(transport.Sent, Is.Empty);
        }

        [Test]
        public void 전송은_보낼_때마다_다시_묻는다()
        {
            // host 가 transport 를 교체한다. 한 번 잡아 두면 사라진 socket 에 계속 쓴다.
            var first = new FakeTransport();
            var second = new FakeTransport();
            var current = (IAgentTransport)first;
            var sink = new WebSocketPulseSink(() => current, () => 1L);

            sink.Send(Reading);
            current = second;
            sink.Send(Reading);

            Assert.That(first.Sent, Has.Count.EqualTo(1));
            Assert.That(second.Sent, Has.Count.EqualTo(1));
        }
    }
}
