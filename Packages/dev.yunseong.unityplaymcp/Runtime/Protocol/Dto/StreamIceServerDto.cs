using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// One ICE server as the orchestration server configured it. Shaped like the browser's
    /// RTCIceServer so both ends of the peer connection are handed the same values.
    /// </summary>
    internal sealed class StreamIceServerDto
    {
        [JsonProperty("urls")]
        public List<string> Urls { get; set; } = new List<string>();

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("credential")]
        public string Credential { get; set; }
    }
}
