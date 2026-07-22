using System;
using System.Collections.Concurrent;
using Artel.Domain;
using WebSocketSharp;

namespace Artel
{
    internal sealed class ArtelWebSocketClient : IArtelWebSocketTransport
    {
        private const string SdkWebSocketPath = "/ws/sdk";

        private readonly string url;
        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();
        private WebSocket client;

        public ArtelWebSocketClient(Server server, string instanceKey)
        {
            url = BuildEndpoint(server, instanceKey).AbsoluteUri;
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

        public bool IsConnected
        {
            get { return client != null && client.ReadyState == WebSocketState.Open; }
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

        internal static Uri BuildEndpoint(Server server, string instanceKey)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(instanceKey))
            {
                throw new ArgumentException("Instance key is required.", nameof(instanceKey));
            }

            var endpoint = new Uri(server.WebSocketBaseUri, SdkWebSocketPath);
            return new UriBuilder(endpoint)
            {
                Query = "instanceKey=" + Uri.EscapeDataString(instanceKey)
            }.Uri;
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
