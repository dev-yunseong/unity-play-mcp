using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ActionErrorDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
