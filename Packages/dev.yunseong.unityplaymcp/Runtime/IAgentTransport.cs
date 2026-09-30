using System;

namespace UnityPlayMcp
{
    internal interface IAgentTransport : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>지금까지 열린 client 연결 수다. 줄지 않는다.</summary>
        /// <remarks>
        /// 새 client 에게 전량 reading 을 보내려면 연결이 새로 열린 것을 알아야 한다.
        /// <see cref="IsConnected"/> 는 server 상태만 알려 준다 (#69).
        /// </remarks>
        int ClientsOpened { get; }
        void Start();
        void Stop();
        bool TryDequeueMessage(out AgentMessage message);
        void Send(string text);
    }
}
