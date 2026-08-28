using System;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine.Networking;

namespace Artel
{
    internal sealed class ArtelSdkRegistrationClient
    {
        private const string RegistrationPath = "/api/sdk/registrations";

        private readonly IJsonCodec jsonCodec;

        public ArtelSdkRegistrationClient(IJsonCodec jsonCodec)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public UnityWebRequest CreateRequest(
            Server server,
            string token,
            string projectId,
            string sdkUuid,
            string instanceName,
            string gameVersion,
            SceneScanReportDto sceneScan = null)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("SDK token is required.", nameof(token));
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id is required.", nameof(projectId));
            }

            if (string.IsNullOrWhiteSpace(sdkUuid))
            {
                throw new ArgumentException("SDK UUID is required.", nameof(sdkUuid));
            }

            var endpoint = new Uri(server.HttpBaseUri, RegistrationPath);
            var body = jsonCodec.Serialize(new SdkRegistrationRequestDto
            {
                ProjectId = projectId,
                SdkUuid = sdkUuid,
                InstanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName.Trim(),
                GameVersion = gameVersion,
                SceneScan = sceneScan
            });
            var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            return request;
        }
    }
}
