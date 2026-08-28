using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ErrorMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
