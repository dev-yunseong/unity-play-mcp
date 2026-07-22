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
                pageServer = new ArtelTestPageServer(bindAddress, httpPort, websocketPort);
                webSocketServer = new ArtelWebSocketServer(bindAddress, websocketPort);
            }

            artelManager.SetWebSocketTransport(webSocketServer, false);

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
