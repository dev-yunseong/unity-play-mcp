using System;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Threading;
using Artel.Domain;
using WebSocketSharp;

namespace Artel
{
    internal sealed class ArtelWebSocketClient : IArtelWebSocketTransport
    {
        private const string SdkWebSocketPath = "/ws/sdk";

        // 서버가 자격증명이나 인스턴스 접근을 거절할 때 붙이는 코드. 같은 URL 로 다시 걸면 같은
        // 대답이 오므로 이 코드만은 재시도하지 않는다.
        private const ushort CredentialsRefusedCloseCode = 4001;

        private const int MaxReconnectAttempts = 8;
        private const double FirstReconnectDelaySeconds = 1;
        private const double MaxReconnectDelaySeconds = 30;

        // 이만큼 열려 있었으면 건강한 세션으로 보고 시도 횟수를 되돌린다. 아래 HandleClose 참고.
        private const double HealthyConnectionSeconds = 60;

        // 유휴 연결을 살아 있는 것으로 보이게 하는 주기. WebSocket.WaitTime 기본값 5초보다 충분히
        // 길어야 ping 이 다음 ping 을 밀지 않는다.
        private const double KeepAliveIntervalSeconds = 30;

        private readonly string url;
        private readonly ConcurrentQueue<ArtelWebSocketMessage> incomingMessages =
            new ConcurrentQueue<ArtelWebSocketMessage>();

        // 소켓 교체와 타이머 예약은 세 스레드가 함께 닿는다: Start/Stop 을 부르는 Unity 메인
        // 스레드, 닫힘과 열림을 올리는 websocket-sharp 수신 스레드, 재시도를 깨우는 타이머
        // 스레드. 상태 전이는 전부 이 자물쇠 아래에서만 한다.
        private readonly object gate = new object();
        private readonly System.Diagnostics.Stopwatch connectionUptime =
            new System.Diagnostics.Stopwatch();

        private WebSocket client;
        private Timer reconnectTimer;
        private Timer keepAliveTimer;
        private int reconnectAttempt;
        private bool stopped;

        public ArtelWebSocketClient(Server server, string token, string instanceId)
        {
            url = BuildEndpoint(server, token, instanceId).AbsoluteUri;
        }

        /// <summary>
        /// 연결을 연다. 이미 열려 있거나 여는 중이면 아무것도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// 살아 있는 소켓이 있을 때만 물러선다. "client 가 null 이 아니면 물러선다"로 두면 끊긴
        /// 소켓이 그 자리를 영원히 차지한다 — client 를 비우는 곳은 Stop 뿐이라, 한 번 끊긴 뒤에는
        /// 오버레이의 연결 버튼이 여기까지 닿아도 아무 일도 일어나지 않으면서 시작 로그만 남는다.
        /// </remarks>
        public void Start()
        {
            lock (gate)
            {
                stopped = false;
                reconnectAttempt = 0;
                DisposeTimer(ref reconnectTimer);

                if (IsLive(client))
                {
                    return;
                }

                Connect();
            }
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

        /// <summary>
        /// 닫힘 코드와 지금까지의 시도 횟수로 다음 재시도까지 기다릴 시간을 정한다.
        /// </summary>
        /// <remarks>
        /// 4001 은 토큰이나 인스턴스 접근이 거절됐다는 뜻이다. 재연결은 같은 URL 을 다시 쓰므로
        /// 대답도 같고, 재시도는 끝나지 않는 고리가 된다. 4002(이미 붙어 있는 인스턴스)는 다르다 —
        /// 앞 연결이 서버에서 정리되면 풀리므로 backoff 를 두고 다시 시도할 값이 있다.
        ///
        /// 시계를 읽지 않는다. 정책이 순수해야 닫힘 코드별 동작을 시간에 기대지 않고 시험할 수 있다.
        /// </remarks>
        internal static bool TryReconnectDelay(ushort closeCode, int attempt, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;

            if (closeCode == CredentialsRefusedCloseCode)
            {
                return false;
            }

            if (attempt < 0 || attempt >= MaxReconnectAttempts)
            {
                return false;
            }

            var seconds = Math.Min(
                FirstReconnectDelaySeconds * Math.Pow(2, attempt),
                MaxReconnectDelaySeconds);
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }

        /// <summary>연결을 열었거나 여는 중인 소켓인지.</summary>
        internal static bool IsLive(WebSocket socket)
        {
            if (socket == null)
            {
                return false;
            }

            return socket.ReadyState == WebSocketState.Connecting ||
                   socket.ReadyState == WebSocketState.Open;
        }

        // ConnectAsync reports nothing to the caller, so without these the socket can fail to
        // open and every layer above still reads as connected. The close code matters most:
        // the server sends 4001 when the token or the instance is refused, and 4002 when that
        // instance already holds a connection.
        private void HandleOpen(WebSocket socket)
        {
            lock (gate)
            {
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                connectionUptime.Restart();
                StartKeepAlive();
            }

            UnityEngine.Debug.Log("[Artel] WebSocket connected.");
        }

        private void HandleError(ErrorEventArgs e)
        {
            UnityEngine.Debug.LogError("[Artel] WebSocket error: " + e.Message);
        }

        private void HandleClose(WebSocket socket, CloseEventArgs e)
        {
            UnityEngine.Debug.LogWarning(
                "[Artel] WebSocket closed: code=" + e.Code + " reason=" + e.Reason);

            lock (gate)
            {
                // 버려 둔 소켓의 늦은 닫힘이 살아 있는 연결의 재시도를 예약하면 안 된다.
                if (!ReferenceEquals(socket, client))
                {
                    return;
                }

                DisposeTimer(ref keepAliveTimer);

                if (stopped)
                {
                    return;
                }

                // 오래 버틴 연결이 끊긴 것은 새 사고다. 앞선 실패들이 쌓아 둔 수열에 얹으면 정상
                // 세션이 몇 시간에 한 번 끊길 때마다 남은 시도가 줄어들어, 결국 재연결을 포기한다.
                //
                // 그렇다고 HandleOpen 에서 되돌리면 반대쪽이 깨진다. 서버는 중복 인스턴스를 핸드셰이크가
                // 끝난 뒤 4002 로 끊으므로 그 경우에도 열림이 먼저 오고, 시도 횟수가 영영
                // 0에 머물러 재시도가 멈추지 않는다. 그래서 기준은 "열렸는가"가 아니라 "버텼는가"다.
                if (connectionUptime.Elapsed.TotalSeconds >= HealthyConnectionSeconds)
                {
                    reconnectAttempt = 0;
                }

                connectionUptime.Reset();

                TimeSpan delay;
                if (!TryReconnectDelay(e.Code, reconnectAttempt, out delay))
                {
                    UnityEngine.Debug.LogError(
                        "[Artel] WebSocket will not reconnect: close code=" + e.Code +
                        ", attempts=" + reconnectAttempt +
                        ". Reconnect from the Artel overlay to try again.");
                    return;
                }

                reconnectAttempt++;
                UnityEngine.Debug.LogWarning(
                    "[Artel] Reconnecting WebSocket in " + delay.TotalSeconds +
                    "s (attempt " + reconnectAttempt + " of " + MaxReconnectAttempts + ").");

                DisposeTimer(ref reconnectTimer);
                reconnectTimer = new Timer(
                    _ => Reconnect(), null, delay, Timeout.InfiniteTimeSpan);
            }
        }

        public bool IsConnected
        {
            get
            {
                var socket = client;
                return socket != null && socket.ReadyState == WebSocketState.Open;
            }
        }

        public bool TryDequeueMessage(out ArtelWebSocketMessage message)
        {
            return incomingMessages.TryDequeue(out message);
        }

        public void Send(string text)
        {
            var socket = client;
            if (socket == null || socket.ReadyState != WebSocketState.Open)
            {
                throw new InvalidOperationException("Artel WebSocket client is not connected.");
            }

            socket.Send(text);
        }

        public void Stop()
        {
            lock (gate)
            {
                // 재시도를 막는 것은 이 표시다. 닫힘 핸들러는 그대로 달려 있고 로그도 그대로 남는다 —
                // client 를 비웠으므로 그 핸들러는 재시도까지 가지 않고 로그만 남기고 돌아선다.
                stopped = true;
                reconnectAttempt = 0;
                DisposeTimer(ref reconnectTimer);
                DisposeTimer(ref keepAliveTimer);
                connectionUptime.Reset();

                if (client == null)
                {
                    return;
                }

                var closing = client;
                client = null;
                closing.CloseAsync();
            }
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

        private void HandleMessage(WebSocket socket, MessageEventArgs eventArgs)
        {
            if (!eventArgs.IsText)
            {
                return;
            }

            if (!ReferenceEquals(socket, client))
            {
                return;
            }

            incomingMessages.Enqueue(new ArtelWebSocketMessage(eventArgs.Data, Send));
        }

        /// <summary>
        /// 새 소켓을 열고, 그 전에 있던 소켓을 놓아준다.
        /// </summary>
        /// <remarks>
        /// gate 를 쥔 채 부른다.
        ///
        /// 핸들러가 이벤트의 sender 대신 자기가 달린 소켓을 캡처해서 받는다. websocket-sharp 이
        /// sender 로 무엇을 넘기는지는 이 어셈블리 밖에서 정해지는 값이고, 자동 재연결 전체가 그
        /// 판정 위에 서 있기 때문이다. 캡처한 참조는 이 파일이 스스로 아는 값이다.
        ///
        /// 예전 소켓을 놓기 전에 client 를 먼저 비운다. Discard 안의 Dispose 가 같은 스레드에서
        /// 닫힘을 올릴 수 있는데, 그때 client 가 아직 그 소켓을 가리키고 있으면 방금 버린 연결이
        /// 재시도를 예약한다.
        /// </remarks>
        private void Connect()
        {
            var previous = client;
            client = null;
            Discard(previous);

            var socket = new WebSocket(url);
            EnableModernTls(socket);
            socket.OnMessage += (sender, e) => HandleMessage(socket, e);
            socket.OnOpen += (sender, e) => HandleOpen(socket);
            socket.OnError += (sender, e) => HandleError(e);
            socket.OnClose += (sender, e) => HandleClose(socket, e);
            client = socket;

            UnityEngine.Debug.Log("[Artel] Connecting WebSocket to " + url);
            socket.ConnectAsync();
        }

        private void Reconnect()
        {
            lock (gate)
            {
                if (stopped || IsLive(client))
                {
                    return;
                }

                Connect();
            }
        }

        /// <summary>
        /// 갈아치운 소켓의 자원을 놓는다.
        /// </summary>
        /// <remarks>
        /// 닫힌 소켓에만 Dispose 를 부른다. 닫히는 중인 소켓은 닫힘 핸드셰이크를 WaitTime 만큼
        /// 기다리는데, 이 자리는 Start 를 거쳐 온 Unity 메인 스레드일 수 있다. 그런 소켓은 스스로
        /// 닫히도록 두고, 이 인스턴스는 client 를 비운 것으로 이미 손을 뗐다.
        /// </remarks>
        private static void Discard(WebSocket socket)
        {
            if (socket == null || socket.ReadyState != WebSocketState.Closed)
            {
                return;
            }

            ((IDisposable)socket).Dispose();
        }

        /// <summary>gate 를 쥔 채 부른다.</summary>
        private void StartKeepAlive()
        {
            DisposeTimer(ref keepAliveTimer);

            var interval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);
            keepAliveTimer = new Timer(_ => SendKeepAlivePing(), null, interval, interval);
        }

        /// <summary>
        /// 조용한 연결도 살아 있음을 중간 프록시에 보인다.
        /// </summary>
        /// <remarks>
        /// 답 없는 ping 하나로 끊지 않는다. pong 이 한 번 늦는 것과 연결이 죽은 것은 다르고,
        /// 후자라면 어차피 닫힘이 온다. 여기서 앞질러 끊으면 멀쩡한 연결을 우리 손으로 버린다.
        /// </remarks>
        private void SendKeepAlivePing()
        {
            WebSocket socket;
            lock (gate)
            {
                socket = client;
                if (stopped || socket == null || socket.ReadyState != WebSocketState.Open)
                {
                    return;
                }
            }

            // Ping 은 pong 을 WaitTime 만큼 기다리며 막힌다. gate 를 놓고 부른다.
            if (!socket.Ping())
            {
                UnityEngine.Debug.LogWarning("[Artel] WebSocket keep-alive ping went unanswered.");
            }
        }

        private static void DisposeTimer(ref Timer timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Dispose();
            timer = null;
        }
    }
}
