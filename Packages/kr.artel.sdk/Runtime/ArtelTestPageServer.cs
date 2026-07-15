using System;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelTestPageServer : MonoBehaviour
    {
        [SerializeField] private bool startServerOnEnable = true;
        [SerializeField] private string bindAddress = "127.0.0.1";
        [SerializeField] private int httpPort = 17310;
        [SerializeField] private int websocketPort = 17311;

        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;

        public string Url
        {
            get { return "http://" + bindAddress + ":" + httpPort + "/"; }
        }

        private void OnEnable()
        {
            if (startServerOnEnable)
            {
                StartServer();
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        public void StartServer()
        {
            if (listener != null)
            {
                return;
            }

            running = true;
            listener = new HttpListener();
            listener.Prefixes.Add("http://" + bindAddress + ":" + httpPort + "/");
            listener.Start();
            listenerThread = new Thread(ListenLoop) { IsBackground = true };
            listenerThread.Start();
            Debug.Log("[Artel] Test page server started at " + Url);
        }

        public void StopServer()
        {
            running = false;
            listener?.Close();
            listener = null;
            listenerThread = null;
        }

        private void ListenLoop()
        {
            while (running && listener != null)
            {
                try
                {
                    var context = listener.GetContext();
                    WritePage(context);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogError("[Artel] Test page request failed: " + exception);
                }
            }
        }

        private void WritePage(HttpListenerContext context)
        {
            var html = ArtelTestPage.Html.Replace("__WS_URL__", "ws://" + bindAddress + ":" + websocketPort + "/ws");
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
    }
}
