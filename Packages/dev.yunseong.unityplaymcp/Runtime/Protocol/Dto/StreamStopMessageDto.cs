using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class StreamStopMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }
    }
}
