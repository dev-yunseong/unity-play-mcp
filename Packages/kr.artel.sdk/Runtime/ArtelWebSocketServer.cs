using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Artel
{
    internal sealed class ArtelWebSocketMessage
    {
        public ArtelWebSocketMessage(string text, Action<string> reply)
        {
            Text = text;
            Reply = reply;
        }

        public string Text { get; private set; }
        public Action<string> Reply { get; private set; }
    }

    internal sealed class ArtelWebSocketServer : IArtelWebSocketTransport
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();
        private readonly Dictionary<string, Action<string>> sendByConnectionId =
            new Dictionary<string, Action<string>>();
        private WebSocketServer server;

        public ArtelWebSocketServer(string bindAddress, int port)
        {
            this.bindAddress = bindAddress;
            this.port = port;
        }

        public void Start()
        {
            if (server != null)
            {
                return;
            }

            server = new WebSocketServer("ws://" + bindAddress + ":" + port);
            server.AddWebSocketService("/ws", CreateBehavior);
            server.Start();
        }

        public bool TryDequeueMessage(out ArtelWebSocketMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(string text)
        {
            lock (sendByConnectionId)
            {
                foreach (var send in sendByConnectionId.Values)
                {
                    send(text);
                }
            }
        }

        public void Stop()
        {
            server?.Stop();
            server = null;

            lock (sendByConnectionId)
            {
                sendByConnectionId.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private ArtelWebSocketBehavior CreateBehavior()
        {
            var behavior = new ArtelWebSocketBehavior();
            behavior.Configure(
                (connectionId, send) =>
                {
                    lock (sendByConnectionId)
                    {
                        sendByConnectionId[connectionId] = send;
                    }
                },
                connectionId =>
                {
                    lock (sendByConnectionId)
                    {
                        sendByConnectionId.Remove(connectionId);
                    }
                },
                (text, reply) => incomingMessages.Enqueue(new ArtelWebSocketMessage(text, reply)));
            return behavior;
        }
    }

    internal sealed class ArtelWebSocketBehavior : WebSocketBehavior
    {
        private Action<string, Action<string>> onOpen;
        private Action<string> onClose;
        private Action<string, Action<string>> onMessage;

        public void Configure(
            Action<string, Action<string>> onOpen,
            Action<string> onClose,
            Action<string, Action<string>> onMessage)
        {
            this.onOpen = onOpen;
            this.onClose = onClose;
            this.onMessage = onMessage;
        }

        protected override void OnOpen()
        {
            onOpen?.Invoke(ID, Send);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            onClose?.Invoke(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                onMessage?.Invoke(e.Data, Send);
            }
        }
    }
}
