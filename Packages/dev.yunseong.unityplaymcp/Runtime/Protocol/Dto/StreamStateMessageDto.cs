using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Reports where one session stands: CONNECTING, LIVE, FAILED or STOPPED.
    ///
    /// FAILED is reported the moment ICE gives up rather than left to time out. Without TURN a
    /// game and a browser on different networks negotiate successfully and then carry no media;
    /// surfaced as an endless "connecting", a network limitation gets filed as a broken stream.
    /// </summary>
    internal sealed class StreamStateMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
