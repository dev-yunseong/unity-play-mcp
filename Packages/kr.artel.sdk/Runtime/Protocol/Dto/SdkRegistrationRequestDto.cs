using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class SdkRegistrationRequestDto
    {
        [JsonProperty("sdkId")]
        public string SdkId { get; set; }
    }
}
