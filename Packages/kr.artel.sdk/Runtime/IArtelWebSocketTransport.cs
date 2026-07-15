using System;

namespace Artel
{
    internal interface IArtelWebSocketTransport : IDisposable
    {
        bool IsConnected { get; }
        void Start();
        void Stop();
        bool TryDequeueMessage(out ArtelWebSocketMessage message);
        void Send(string text);
    }
}
