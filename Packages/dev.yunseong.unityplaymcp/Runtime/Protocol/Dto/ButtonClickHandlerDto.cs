using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ButtonClickHandlerDto
    {
        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("targetType")]
        public string TargetType { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }
    }
}
