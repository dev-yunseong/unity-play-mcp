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
            pageServer = new ArtelTestPageServer(bindAddress, httpPort, websocketPort);
            webSocketServer = new ArtelWebSocketServer(bindAddress, websocketPort);

            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }

            if (artelManager == null)
            {
                Debug.LogError("[Artel] ArtelTestPageManager requires an ArtelManager reference.");
                enabled = false;
                return;
            }

            artelManager.SetWebSocketTransport(webSocketServer, false);
        }

        private void OnEnable()
        {
            if (startOnEnable)
            {
                StartServers();
            }
        }

        private void OnDisable()
        {
            StopServers();
        }

        public void StartServers()
        {
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
