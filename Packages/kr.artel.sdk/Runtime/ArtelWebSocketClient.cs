using System;
using System.Collections.Concurrent;
using System.Security.Authentication;
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

        public ArtelWebSocketClient(Server server, string token, string instanceId)
        {
            url = BuildEndpoint(server, token, instanceId).AbsoluteUri;
        }

        public void Start()
        {
            if (client != null)
            {
                return;
            }

            client = new WebSocket(url);
            EnableModernTls(client);
            client.OnMessage += OnMessage;
            client.OnOpen += OnOpen;
            client.OnError += OnError;
            client.OnClose += OnClose;
            UnityEngine.Debug.Log("[Artel] Connecting WebSocket to " + url);
            client.ConnectAsync();
        }

        // websocket-sharp opens its own TcpClient and negotiates TLS through Mono's SslStream,
        // so the protocol list comes from this library and not from the native stack behind
        // UnityWebRequest. Its default is SslProtocols.Default, which is Ssl3 | TLS 1.0. A proxy
        // serving TLS 1.2 and up answers that ClientHello with a protocol_version alert, and the
        // socket closes with code 1015 before the HTTP upgrade is ever sent. REST calls to the
        // same host keep working, so the failure reads as a WebSocket outage rather than a TLS one.
        //
        // TLS 1.3 is left out deliberately: Unity's Mono TLS provider does not implement it, and
        // requesting it fails the handshake instead of falling back to 1.2.
        internal static void EnableModernTls(WebSocket socket)
        {
            if (!socket.IsSecure)
            {
                return;
            }

            socket.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
        }

        // ConnectAsync reports nothing to the caller, so without these the socket can fail to
        // open and every layer above still reads as connected. The close code matters most:
        // the server sends 4001 when the token or the instance is refused, and 4002 when that
        // instance already holds a connection.
        private void OnOpen(object sender, EventArgs e)
        {
            UnityEngine.Debug.Log("[Artel] WebSocket connected.");
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            UnityEngine.Debug.LogError("[Artel] WebSocket error: " + e.Message);
        }

        private void OnClose(object sender, CloseEventArgs e)
        {
            UnityEngine.Debug.LogWarning(
                "[Artel] WebSocket closed: code=" + e.Code + " reason=" + e.Reason);
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

        // ponytail: 토큰이 쿼리에 실린다. WebSocketSharp의 커스텀 헤더로 옮기려면 서버
        // 핸드셰이크도 같이 바꿔야 하므로, 그때 양쪽을 함께 옮긴다.
        internal static Uri BuildEndpoint(Server server, string token, string instanceId)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("SDK token is required.", nameof(token));
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id is required.", nameof(instanceId));
            }

            var endpoint = new Uri(server.WebSocketBaseUri, SdkWebSocketPath);
            return new UriBuilder(endpoint)
            {
                Query = "token=" + Uri.EscapeDataString(token) +
                        "&instanceId=" + Uri.EscapeDataString(instanceId)
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
