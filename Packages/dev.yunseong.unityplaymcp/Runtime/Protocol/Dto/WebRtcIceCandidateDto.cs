using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Shaped like the browser's RTCIceCandidateInit. The orchestration server never opens this —
    /// it forwards the raw body — so the two peers are the only readers.
    /// </summary>
    internal sealed class WebRtcIceCandidateDto
    {
        [JsonProperty("candidate")]
        public string Candidate { get; set; }

        [JsonProperty("sdpMid")]
        public string SdpMid { get; set; }

        [JsonProperty("sdpMLineIndex")]
        public int? SdpMLineIndex { get; set; }
    }
}
