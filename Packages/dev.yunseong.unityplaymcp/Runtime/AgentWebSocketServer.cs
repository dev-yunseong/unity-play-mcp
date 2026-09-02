using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace UnityPlayMcp
{
    internal sealed class AgentMessage
    {
        public AgentMessage(string text, Action<string> reply)
        {
            Text = text;
            Reply = reply;
        }

        public string Text { get; private set; }
        public Action<string> Reply { get; private set; }
    }

    internal sealed class AgentWebSocketServer : IAgentTransport
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly ConcurrentQueue<AgentMessage> incomingMessages =
            new ConcurrentQueue<AgentMessage>();
        private readonly Dictionary<string, Action<string>> sendByConnectionId =
            new Dictionary<string, Action<string>>();
        private WebSocketServer server;

        public AgentWebSocketServer(string bindAddress, int port)
        {
            this.bindAddress = bindAddress;
            this.port = port;
        }

        public bool IsConnected
        {
            get { return server != null; }
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

        public bool TryDequeueMessage(out AgentMessage message)
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

        private AgentSocketBehavior CreateBehavior()
        {
            var behavior = new AgentSocketBehavior();
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
                (text, reply) => incomingMessages.Enqueue(new AgentMessage(text, reply)));
            return behavior;
        }
    }

    internal sealed class AgentSocketBehavior : WebSocketBehavior
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
