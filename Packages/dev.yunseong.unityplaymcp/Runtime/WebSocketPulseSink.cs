using System;
using UnityPlayMcp.Affordances.Live;

namespace UnityPlayMcp
{
    /// <summary>
    /// pulse 를 이미 열려 있는 /ws/sdk 연결에 얹는다.
    /// </summary>
    /// <remarks>
    /// 별도 소켓을 열면 인증, 재연결, 끊김 처리가 중복되므로 기존 연결을 쓴다.
    ///
    /// <see cref="StreamSignalSender"/> 처럼 보낼 때마다 transport 를 다시 조회한다.
    /// manager 가 transport 를 교체하거나 비운 뒤 닫힌 소켓에 쓰지 않기 위해서다.
    /// </remarks>
    internal sealed class WebSocketPulseSink : IPulseSink
    {
        private readonly Func<IAgentTransport> currentTransport;
        private readonly Func<long> nextMessageId;

        public WebSocketPulseSink(
            Func<IAgentTransport> currentTransport, Func<long> nextMessageId)
        {
            this.currentTransport = currentTransport
                ?? throw new ArgumentNullException(nameof(currentTransport));
            this.nextMessageId = nextMessageId
                ?? throw new ArgumentNullException(nameof(nextMessageId));
        }

        /// <summary>
        /// pulse 하나를 프레임으로 감싸 보낸다.
        /// </summary>
        /// <remarks>
        /// 보낼 수 없으면 예외를 던진다. <see cref="Pulse"/> 는 실패한 전달을 보고 다음 pulse 를 전량으로 만든다.
        /// 조용히 넘기면 이 복구가 돌지 않아 MCP server 가 빠진 차이를 영영 받지 못한다.
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
                    "The Unity Play MCP connection is not open, so this reading cannot be sent.");
            }

            transport.Send(Framed(document));
        }

        /// <summary>
        /// 문서 앞에 <c>type</c> 과 <c>id</c> 필드를 붙인다.
        /// </summary>
        /// <remarks>
        /// pulse 가 <c>{"schema":</c> 로 시작한다는 것은 <see cref="LiveState"/> 가 보장한다.
        /// 다시 파싱하고 직렬화하는 비용을 피하려고 문자열을 이어 붙인다.
        ///
        /// <c>type</c> 은 orchestration 의 handler 선택에 쓰이고, <c>id</c> 는 재전송된 pulse 가
        /// 로그에 한 번만 기록되게 한다.
        /// </remarks>
        private string Framed(string document)
        {
            return "{\"type\":\"PULSE\",\"id\":" + nextMessageId() + "," + document.Substring(1);
        }
    }
}
