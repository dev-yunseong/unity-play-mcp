using System;
using Artel.Affordances.Live;

namespace Artel
{
    /// <summary>
    /// 판독을 이미 열려 있는 /ws/sdk 연결에 얹는다.
    /// </summary>
    /// <remarks>
    /// 새 소켓을 열지 않는다. 판독은 게임이 도는 동안만 흐르고 그 창은 이미 연결이 서 있는
    /// 창이므로, 두 번째 소켓은 두 번째 인증·두 번째 재연결·두 번째 끊김을 만들 뿐이다.
    ///
    /// 전송은 <see cref="StreamSignalSender"/> 가 하는 것과 같은 모양이다. 그쪽 주석이 적어
    /// 둔 이유가 여기에도 그대로 적용된다 — 전송을 보낼 때마다 다시 물어보는 것은, 매니저가
    /// 프로세스가 사는 동안 전송을 갈아끼우거나 비우고 그 교체를 넘어 사는 채널이 사라진
    /// 소켓에 계속 쓰면 안 되기 때문이다.
    /// </remarks>
    internal sealed class WebSocketPulseSink : IPulseSink
    {
        private readonly Func<IArtelWebSocketTransport> currentTransport;
        private readonly Func<long> nextMessageId;

        public WebSocketPulseSink(
            Func<IArtelWebSocketTransport> currentTransport, Func<long> nextMessageId)
        {
            this.currentTransport = currentTransport
                ?? throw new ArgumentNullException(nameof(currentTransport));
            this.nextMessageId = nextMessageId
                ?? throw new ArgumentNullException(nameof(nextMessageId));
        }

        /// <summary>
        /// 판독 하나를 프레임으로 감싸 보낸다.
        /// </summary>
        /// <remarks>
        /// 던지는 것이 그만두라는 말이 아니다. <see cref="Pulse"/> 는 실패한 전달을 잃은 것으로
        /// 표시하고 다음 판독을 전량으로 만드는데, 조용히 성공한 척하면 그 복구가 돌지 않아
        /// 독자가 받지 못한 차이에 대해 영영 틀린 채로 남는다. 그래서 보낼 수 없을 때는
        /// 말한다.
        /// </remarks>
        public void Send(string document)
        {
            if (string.IsNullOrEmpty(document))
            {
                return;
            }

            var transport = currentTransport();

            if (transport == null || !transport.IsConnected)
            {
                throw new InvalidOperationException(
                    "The Artel connection is not open, so this reading cannot be sent.");
            }

            transport.Send(Framed(document));
        }

        /// <summary>
        /// 문서 앞에 봉투 두 칸을 끼운다.
        /// </summary>
        /// <remarks>
        /// 문자열을 그대로 이어 붙이는 것은 판독이 <c>{"schema":</c> 로 시작한다는 것이
        /// <see cref="LiveState"/> 안에서 한 자리에 적혀 있기 때문이다. 파싱해서 다시
        /// 직렬화하면 전량 판독 18 KB 를 초당 한 번 두 번씩 훑게 되고, 이 패키지가 JSON 을
        /// 손으로 쓰는 이유가 바로 그 값을 치르지 않으려는 것이다.
        ///
        /// <c>type</c> 은 orchestration 의 핸들러 등록제가 읽는 값이고, <c>id</c> 는 같은
        /// 판독의 재전송이 로그에서 한 번만 적재되게 하는 값이다.
        /// </remarks>
        private string Framed(string document)
        {
            return "{\"type\":\"PULSE\",\"id\":" + nextMessageId() + "," + document.Substring(1);
        }
    }
}
