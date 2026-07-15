using System;

namespace Artel
{
    internal interface IArtelWebSocketTransport : IDisposable
    {
        void Start();
        void Stop();
        bool TryDequeueMessage(out ArtelWebSocketMessage message);
        void Send(string text);
    }
}
