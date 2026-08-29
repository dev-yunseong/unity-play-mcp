using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class WebRtcAnswerMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("sdp")]
        public string Sdp { get; set; }
    }
}
