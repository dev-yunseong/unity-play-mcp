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

        public UnityWebRequest CreateRequest(Server server, string instanceKey, string sdkUuid, string gameVersion)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrWhiteSpace(instanceKey))
            {
                throw new ArgumentException("Instance key is required.", nameof(instanceKey));
            }

            if (string.IsNullOrWhiteSpace(sdkUuid))
            {
                throw new ArgumentException("SDK UUID is required.", nameof(sdkUuid));
            }

            var endpoint = new Uri(server.HttpBaseUri, RegistrationPath);
            var body = jsonCodec.Serialize(new SdkRegistrationRequestDto
            {
                InstanceKey = instanceKey,
                SdkUuid = sdkUuid,
                GameVersion = gameVersion
            });
            var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }
    }
}
