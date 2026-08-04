using System;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine.Networking;

namespace Artel.Auth
{
    /// <summary>
    /// 로그인 code를 토큰으로 바꾸고, 그 토큰으로 고를 수 있는 프로젝트를 물어본다.
    /// </summary>
    internal sealed class ArtelSdkAuthClient
    {
        private const string TokenPath = "/api/auth/sdk/token";
        private const string RefreshPath = "/api/auth/sdk/token/refresh";
        private const string ProjectsPath = "/api/sdk/projects";

        private readonly IJsonCodec jsonCodec;

        public ArtelSdkAuthClient(IJsonCodec jsonCodec)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public UnityWebRequest CreateTokenRequest(Server server, string code, string codeVerifier)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Login code is required.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(codeVerifier))
            {
                throw new ArgumentException("Code verifier is required.", nameof(codeVerifier));
            }

            var endpoint = new Uri(server.HttpBaseUri, TokenPath);
            var body = jsonCodec.Serialize(new SdkTokenRequestDto
            {
                Code = code,
                CodeVerifier = codeVerifier
            });
            var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        /// <summary>
        /// 만료된 SDK 토큰을 다시 받는다. 자격증명은 refresh 토큰뿐이라 세션도 헤더도 필요 없다.
        /// </summary>
        public UnityWebRequest CreateRefreshRequest(Server server, string refreshToken)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
            }

            var endpoint = new Uri(server.HttpBaseUri, RefreshPath);
            var body = jsonCodec.Serialize(new SdkRefreshRequestDto { RefreshToken = refreshToken });
            var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        public UnityWebRequest CreateProjectsRequest(Server server, string token)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("SDK token is required.", nameof(token));
            }

            var endpoint = new Uri(server.HttpBaseUri, ProjectsPath);
            var request = UnityWebRequest.Get(endpoint.AbsoluteUri);
            request.SetRequestHeader("Authorization", "Bearer " + token);
            return request;
        }
    }
}
