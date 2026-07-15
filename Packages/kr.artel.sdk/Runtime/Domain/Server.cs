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
