using System;
using System.Collections.Concurrent;
using WebSocketSharp;

namespace Artel
{
    internal sealed class ArtelWebSocketClient : IArtelWebSocketTransport
    {
        private readonly string url;
        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();
        private WebSocket client;

        public ArtelWebSocketClient(Uri endpoint)
        {
            url = endpoint?.AbsoluteUri ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public void Start()
        {
            if (client != null)
            {
                return;
            }

            client = new WebSocket(url);
            client.OnMessage += OnMessage;
            client.ConnectAsync();
        }

        public bool TryDequeueMessage(out ArtelWebSocketMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(string text)
        {
            if (client == null || client.ReadyState != WebSocketState.Open)
            {
                throw new InvalidOperationException("Artel WebSocket client is not connected.");
            }

            client.Send(text);
        }

        public void Stop()
        {
            if (client == null)
            {
                return;
            }

            client.OnMessage -= OnMessage;
            client.CloseAsync();
            client = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnMessage(object sender, MessageEventArgs eventArgs)
        {
            if (eventArgs.IsText)
            {
                incomingMessages.Enqueue(new ArtelWebSocketMessage(eventArgs.Data, Send));
            }
        }
    }
}
