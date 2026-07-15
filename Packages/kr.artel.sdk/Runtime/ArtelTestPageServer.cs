using System;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Artel
{
    internal sealed class ArtelTestPageServer : IDisposable
    {
        private readonly string bindAddress;
        private readonly int httpPort;
        private readonly string websocketUrl;
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;

        public ArtelTestPageServer(string bindAddress, int httpPort, int websocketPort)
        {
            this.bindAddress = bindAddress;
            this.httpPort = httpPort;
            websocketUrl = "ws://" + bindAddress + ":" + websocketPort + "/ws";
        }

        public string Url
        {
            get { return "http://" + bindAddress + ":" + httpPort + "/"; }
        }

        public void Start()
        {
            if (listener != null)
            {
                return;
            }

            running = true;
            listener = new HttpListener();
            listener.Prefixes.Add(Url);
            listener.Start();
            listenerThread = new Thread(ListenLoop) { IsBackground = true };
            listenerThread.Start();
        }

        public void Stop()
        {
            running = false;
            listener?.Close();
            listener = null;
            listenerThread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void ListenLoop()
        {
            while (running && listener != null)
            {
                try
                {
                    WritePage(listener.GetContext());
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
            var html = ArtelTestPage.Html.Replace("__WS_URL__", websocketUrl);
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
    }
}
