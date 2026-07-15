using System;
using UnityEngine;

namespace Artel.Domain
{
    [Serializable]
    public sealed class Server
    {
        private const string SdkRegistrationPath = "/api/sdkId";
        private const string SdkWebSocketPath = "/ws/sdk";

        [SerializeField] private string httpBaseUrl = string.Empty;
        [SerializeField] private string websocketBaseUrl = string.Empty;

        public Server()
        {
        }

        public Server(string httpBaseUrl, string websocketBaseUrl)
        {
            this.httpBaseUrl = httpBaseUrl;
            this.websocketBaseUrl = websocketBaseUrl;
        }

        public Uri SdkRegistrationUri
        {
            get { return BuildUri(httpBaseUrl, SdkRegistrationPath, "http", "https"); }
        }

        public Uri GetSdkWebSocketUri(string sdkId)
        {
            if (string.IsNullOrWhiteSpace(sdkId))
            {
                throw new ArgumentException("SDK ID is required.", nameof(sdkId));
            }

            var endpoint = BuildUri(websocketBaseUrl, SdkWebSocketPath, "ws", "wss");
            return new UriBuilder(endpoint)
            {
                Query = "sdkId=" + Uri.EscapeDataString(sdkId)
            }.Uri;
        }

        private static Uri BuildUri(string baseUrl, string path, string scheme, string secureScheme)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("Server base URL must be an absolute URL: " + baseUrl);
            }

            if (baseUri.Scheme != scheme && baseUri.Scheme != secureScheme)
            {
                throw new InvalidOperationException(
                    "Server URL scheme must be " + scheme + " or " + secureScheme + ": " + baseUrl);
            }

            return new Uri(baseUri.ToString().TrimEnd('/') + path, UriKind.Absolute);
        }
    }
}
