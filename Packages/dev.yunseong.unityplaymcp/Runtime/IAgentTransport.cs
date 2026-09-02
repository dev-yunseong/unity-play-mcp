using System;

namespace UnityPlayMcp
{
    internal interface IAgentTransport : IDisposable
    {
        bool IsConnected { get; }
        void Start();
        void Stop();
        bool TryDequeueMessage(out AgentMessage message);
        void Send(string text);
    }
}
