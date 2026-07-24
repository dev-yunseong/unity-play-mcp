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

        // 스캔이 실패한 등록도 유효해야 하므로 null이면 필드 자체를 보내지 않는다.
        [JsonProperty("sceneScan", NullValueHandling = NullValueHandling.Ignore)]
        public SceneScanReportDto SceneScan { get; set; }
    }
}
