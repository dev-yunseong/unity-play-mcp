using System;

namespace UnityPlayMcp
{
    internal interface IAgentTransport : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>이 transport 에 지금까지 열린 client 연결의 수. 줄지 않는다.</summary>
        /// <remarks>
        /// <see cref="IsConnected"/> 는 server 가 서 있는지만 말하고 누가 붙었는지는 말하지 않는다. 새 client 가 붙은 것을 알아야
        /// 그 client 에게 전량 reading 을 보낼 수 있다 (#69).
        /// </remarks>
        int ClientsOpened { get; }
        void Start();
        void Stop();
        bool TryDequeueMessage(out AgentMessage message);
        void Send(string text);
    }
}
