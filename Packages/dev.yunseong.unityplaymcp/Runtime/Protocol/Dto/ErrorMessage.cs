using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    public sealed class ErrorMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
