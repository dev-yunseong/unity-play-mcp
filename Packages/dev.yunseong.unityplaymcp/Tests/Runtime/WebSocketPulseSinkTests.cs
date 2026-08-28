using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// 판독이 기존 연결로 나가는 모양을 고정한다 (ARTEL-399).
    /// </summary>
    /// <remarks>
    /// 단언 대상이 "보냈는가"가 아니라 <em>무엇을 보냈는가</em>인 이유는, 이 문자열을 읽는
    /// 쪽이 이 저장소 밖에 둘 있기 때문이다 — orchestration 의 타입별 핸들러 등록제가
    /// <c>type</c> 을 보고 고르고, 그 뒤 agent 가 나머지를 판독 문서로 읽는다. 봉투를 끼우는
    /// 방식이 문자열 이어붙이기라 조용히 깨질 수 있고, 그때 이 테스트가 가장 먼저 실패한다.
    /// </remarks>
    public sealed class WebSocketPulseSinkTests
    {
        private sealed class FakeTransport : IArtelWebSocketTransport
        {
            internal readonly List<string> Sent = new List<string>();
            internal bool Connected = true;

            public bool IsConnected { get { return Connected; } }

            public void Start() { }

            public void Stop() { }

            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
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
        public void 판독이_PULSE_프레임으로_나간다()
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
        public void 판독_본문은_한_글자도_바뀌지_않는다()
        {
            var transport = new FakeTransport();
            var sink = new WebSocketPulseSink(() => transport, () => 1L);

            sink.Send(Reading);

            // 봉투 두 칸을 걷어내면 넣은 것이 그대로 남아야 한다.
            var framed = transport.Sent[0];
            var body = "{" + framed.Substring(framed.IndexOf("\"schema\"", StringComparison.Ordinal));
            Assert.That(body, Is.EqualTo(Reading));
        }

        [Test]
        public void 판독마다_id_가_올라간다()
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
            // 조용히 삼키면 Pulse 의 손실 복구가 돌지 않는다. 그러면 독자는 받지 못한 차이에
            // 대해 다음 전량 판독이 올 때까지 틀린 채로 남는다.
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
            // 매니저가 전송을 갈아끼운다. 한 번 잡아 두면 사라진 소켓에 계속 쓰게 된다.
            var first = new FakeTransport();
            var second = new FakeTransport();
            var current = (IArtelWebSocketTransport)first;
            var sink = new WebSocketPulseSink(() => current, () => 1L);

            sink.Send(Reading);
            current = second;
            sink.Send(Reading);

            Assert.That(first.Sent, Has.Count.EqualTo(1));
            Assert.That(second.Sent, Has.Count.EqualTo(1));
        }
    }
}
