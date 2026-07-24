using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The SDK offers. That keeps the media-direction decision on the side that owns the source
    /// and spares the browser from declaring a recvonly transceiver up front.
    /// </summary>
    internal sealed class WebRtcOfferMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("sdp")]
        public string Sdp { get; set; }
    }
}
