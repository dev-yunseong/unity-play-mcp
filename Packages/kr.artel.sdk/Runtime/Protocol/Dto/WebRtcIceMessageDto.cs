using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class WebRtcIceMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("candidate")]
        public WebRtcIceCandidateDto Candidate { get; set; }
    }
}
