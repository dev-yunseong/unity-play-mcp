using Artel.Capture;
using UnityEngine;
using UnityEngine.Serialization;

namespace Artel
{
    public sealed class ArtelTestPageManager : MonoBehaviour
    {
        [SerializeField] private ArtelManager artelManager;
        [FormerlySerializedAs("startServerOnEnable")]
        [SerializeField] private bool startOnEnable = true;
        [SerializeField] private string bindAddress = "127.0.0.1";
        [SerializeField] private int httpPort = 17310;
        [SerializeField] private int websocketPort = 17311;

        private ArtelTestPageServer pageServer;
        private ArtelWebSocketServer webSocketServer;
        private LocalCaptureStore captures;

        public string Url
        {
            get { return pageServer?.Url ?? "http://" + bindAddress + ":" + httpPort + "/"; }
        }

        private void Awake()
        {
            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }

            if (artelManager == null)
            {
                Debug.LogError("[Artel] ArtelTestPageManager requires an ArtelManager reference.");
                enabled = false;
            }
        }

        /// <summary>
        /// Taking the transport used to happen in <c>Awake</c>, which Unity calls even on a
        /// disabled component. Unchecking this component therefore did nothing: it still replaced
        /// the game's connection to the orchestration server with a local test-page socket, and
        /// the only symptom was a game that never appeared online. <c>OnEnable</c> runs only when
        /// the component is actually on, so the checkbox now means what it looks like it means.
        /// </summary>
        private void OnEnable()
        {
            if (artelManager == null)
            {
                return;
            }

            if (artelManager.HasWebSocketTransport)
            {
                Debug.LogWarning(
                    "[Artel] The game already has a WebSocket transport, " +
                    "so the test page will not serve it.");
                return;
            }

            if (pageServer == null)
            {
                captures = new LocalCaptureStore();
                pageServer = new ArtelTestPageServer(bindAddress, httpPort, websocketPort, captures);
                webSocketServer = new ArtelWebSocketServer(bindAddress, websocketPort);
            }

            artelManager.SetWebSocketTransport(webSocketServer, false);

            // 캡처도 전송과 함께 넘겨받는다. 오케스트레이션의 티켓 엔드포인트는 실행 중인 QA 가 없는
            // 인스턴스를 409 로 거절하고, 테스트 페이지에서 찍는 캡처는 전부 그 경우다. 이 업로더는 티켓을
            // 스스로 끊고 이미지를 위의 페이지 서버에서 내주므로, 실행도 세션도 없이 브라우저까지 닿는다.
            artelManager.SetCaptureUploader(new LocalCaptureUploader(captures, () => Url));

            if (startOnEnable)
            {
                StartServers();
            }
        }

        private void OnDisable()
        {
            if (webSocketServer == null)
            {
                return;
            }

            StopServers();

            // Hand the connection back, so switching this off at runtime lets the game reach the
            // orchestration server instead of leaving it wired to a stopped local socket.
            if (artelManager != null)
            {
                artelManager.ClearWebSocketTransport(webSocketServer);

                // 같은 이유로 캡처도 돌려준다. 멈춘 페이지 서버를 가리키는 URL 을 계속 내주면 QA 실행이
                // 근거로 못 읽는 이미지를 성공으로 기록한다.
                artelManager.RestoreCaptureUploader();
            }
        }

        public void StartServers()
        {
            if (webSocketServer == null)
            {
                Debug.LogWarning("[Artel] Test page servers are not available while this component is off.");
                return;
            }

            webSocketServer.Start();
            pageServer.Start();
            Debug.Log("[Artel] Test page servers started at " + Url);
        }

        public void StopServers()
        {
            pageServer?.Stop();
            webSocketServer?.Stop();
            Debug.Log("[Artel] Test page servers stopped.");
        }
    }
}
