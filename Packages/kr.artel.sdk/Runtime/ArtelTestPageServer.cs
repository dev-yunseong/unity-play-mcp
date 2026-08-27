using System;
using System.Net;
using System.Text;
using System.Threading;
using Artel.Capture;
using UnityEngine;

namespace Artel
{
    internal sealed class ArtelTestPageServer : IDisposable
    {
        /// <summary>
        /// 캡처 하나를 가리키는 경로의 앞부분. 뒤에 캡처 id 가 붙는다.
        /// </summary>
        /// <remarks>
        /// <see cref="LocalCaptureUploader"/> 가 돌려주는 URL 과 여기서 받는 경로가 같은 문자열이어야
        /// 하므로 한 곳에 둔다.
        /// </remarks>
        public const string CapturePath = "/captures/";

        private readonly string bindAddress;
        private readonly int httpPort;
        private readonly string websocketUrl;
        private readonly LocalCaptureStore captures;
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;

        public ArtelTestPageServer(
            string bindAddress, int httpPort, int websocketPort, LocalCaptureStore captures)
        {
            this.bindAddress = bindAddress;
            this.httpPort = httpPort;
            this.captures = captures;
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
                    Route(listener.GetContext());
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

        private void Route(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (captures != null && path.StartsWith(CapturePath, StringComparison.Ordinal))
            {
                WriteCapture(context, path.Substring(CapturePath.Length));
                return;
            }

            // Everything else is the page. A test page has one document and no favicon, and a
            // 404 for the odd stray request would only fill the console.
            WritePage(context);
        }

        private void WriteCapture(HttpListenerContext context, string captureId)
        {
            if (!captures.TryGet(captureId, out var capture))
            {
                // 오래된 캡처는 store 가 밀어냈다. 페이지를 새로 고쳤을 때 지난 이미지가 여기로 떨어지므로
                // 조용히 404 로 답한다.
                context.Response.StatusCode = 404;
                context.Response.OutputStream.Close();
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = capture.ContentType;
            context.Response.ContentLength64 = capture.Bytes.Length;

            // 같은 id 는 같은 바이트다 — store 가 id 를 재사용하지 않는다. 그래도 캐시를 막는 것은
            // 이 페이지가 렌더링 버그를 보려고 존재하기 때문이다. 브라우저가 지난 프레임을 보여주면
            // 그 버그를 못 본다.
            context.Response.Headers.Add("Cache-Control", "no-store");
            context.Response.OutputStream.Write(capture.Bytes, 0, capture.Bytes.Length);
            context.Response.OutputStream.Close();
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
