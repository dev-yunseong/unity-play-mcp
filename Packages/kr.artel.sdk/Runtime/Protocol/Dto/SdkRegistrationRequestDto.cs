using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class SdkRegistrationRequestDto
    {
        [JsonProperty("instanceKey")]
        public string InstanceKey { get; set; }

        [JsonProperty("sdkUuid")]
        public string SdkUuid { get; set; }

        [JsonProperty("gameVersion")]
        public string GameVersion { get; set; }
    }
}
