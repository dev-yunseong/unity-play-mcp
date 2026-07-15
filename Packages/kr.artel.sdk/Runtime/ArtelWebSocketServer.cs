using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Artel
{
    internal interface IArtelWebSocketServer : IDisposable
    {
        void Start();
        bool TryDequeueMessage(out ArtelClientMessage message);
        void Send(ArtelConnection connection, string text);
        void SendToAll(string text);
    }

    internal static class ArtelWebSocketServerFactory
    {
        public static IArtelWebSocketServer Create(string bindAddress, int port)
        {
            return new WebSocketSharpArtelServer(bindAddress, port);
        }
    }

    internal sealed class ArtelConnection
    {
        private readonly Action<string> sendText;

        public ArtelConnection(string id, Action<string> sendText)
        {
            Id = id;
            this.sendText = sendText;
        }

        public string Id { get; private set; }

        public void Send(string text)
        {
            sendText?.Invoke(text);
        }
    }

    internal sealed class ArtelClientMessage
    {
        public ArtelClientMessage(ArtelConnection connection, string text)
        {
            Connection = connection;
            Text = text;
        }

        public ArtelConnection Connection { get; private set; }
        public string Text { get; private set; }
    }

    internal sealed class WebSocketSharpArtelServer : IArtelWebSocketServer
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly ConcurrentQueue<ArtelClientMessage> incomingMessages = new ConcurrentQueue<ArtelClientMessage>();
        private readonly Dictionary<string, ArtelConnection> connectionsById = new Dictionary<string, ArtelConnection>();
        private WebSocketServer server;

        public WebSocketSharpArtelServer(string bindAddress, int port)
        {
            this.bindAddress = bindAddress;
            this.port = port;
        }

        public void Start()
        {
            server = new WebSocketServer("ws://" + bindAddress + ":" + port);
            server.AddWebSocketService("/ws", () =>
            {
                var behavior = new ArtelWebSocketBehavior();
                behavior.Configure(
                    OnClientConnected,
                    OnClientDisconnected,
                    (connection, text) => incomingMessages.Enqueue(new ArtelClientMessage(connection, text)));
                return behavior;
            });
            server.Start();
        }

        public bool TryDequeueMessage(out ArtelClientMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(ArtelConnection connection, string text)
        {
            connection?.Send(text);
        }

        public void SendToAll(string text)
        {
            lock (connectionsById)
            {
                foreach (var connection in connectionsById.Values)
                {
                    connection.Send(text);
                }
            }
        }

        public void Dispose()
        {
            server?.Stop();
            server = null;

            lock (connectionsById)
            {
                connectionsById.Clear();
            }
        }

        private void OnClientConnected(ArtelConnection connection)
        {
            lock (connectionsById)
            {
                connectionsById[connection.Id] = connection;
            }
        }

        private void OnClientDisconnected(string connectionId)
        {
            lock (connectionsById)
            {
                connectionsById.Remove(connectionId);
            }
        }
    }

    internal sealed class ArtelWebSocketBehavior : WebSocketBehavior
    {
        private Action<ArtelConnection> onOpen;
        private Action<string> onClose;
        private Action<ArtelConnection, string> onMessage;
        private ArtelConnection connection;

        public void Configure(
            Action<ArtelConnection> onOpen,
            Action<string> onClose,
            Action<ArtelConnection, string> onMessage)
        {
            this.onOpen = onOpen;
            this.onClose = onClose;
            this.onMessage = onMessage;
        }

        protected override void OnOpen()
        {
            connection = new ArtelConnection(ID, Send);
            onOpen?.Invoke(connection);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            onClose?.Invoke(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                onMessage?.Invoke(connection, e.Data);
            }
        }
    }
}
