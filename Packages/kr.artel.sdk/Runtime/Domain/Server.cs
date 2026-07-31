using System;
using UnityEngine;

namespace Artel.Domain
{
    [Serializable]
    public sealed class Server
    {
        [SerializeField] private bool secure = true;
        [SerializeField] private string host = string.Empty;
        [SerializeField] private int port = 443;

        // 로그인 중계 페이지는 오케스트레이션 서버가 아니라 웹 콘솔에 있다. 호스트도 포트도
        // 다르므로(로컬 5173, 배포 console.artel.kr) 위 세 값에서 유도할 수 없다.
        [SerializeField] private string frontendOrigin = "http://localhost:5173";

        public Server()
        {
        }

        public Server(bool secure, string host, int port)
        {
            this.secure = secure;
            this.host = host;
            this.port = port;
        }

        public Uri HttpBaseUri
        {
            get { return BuildBaseUri(secure ? "https" : "http"); }
        }

        public Uri WebSocketBaseUri
        {
            get { return BuildBaseUri(secure ? "wss" : "ws"); }
        }

        public Uri FrontendBaseUri
        {
            get
            {
                if (string.IsNullOrWhiteSpace(frontendOrigin))
                {
                    throw new InvalidOperationException("Frontend origin is required.");
                }

                return new Uri(frontendOrigin.Trim(), UriKind.Absolute);
            }
        }

        private Uri BuildBaseUri(string scheme)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException("Server host is required.");
            }

            if (port < 1 || port > 65535)
            {
                throw new InvalidOperationException("Server port must be between 1 and 65535: " + port);
            }

            return new UriBuilder(scheme, host.Trim(), port).Uri;
        }
    }
}
