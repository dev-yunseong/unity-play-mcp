using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Opens one watching session.
    ///
    /// <see cref="IceServers"/> is delivered here rather than compiled into the SDK. The SDK ships
    /// to customers, so a baked default would have every customer's game contacting a third-party
    /// STUN host; keeping it on the wire makes that a deployment choice.
    /// </summary>
    internal sealed class StreamStartMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("iceServers")]
        public List<StreamIceServerDto> IceServers { get; set; } = new List<StreamIceServerDto>();

        [JsonProperty("video")]
        public StreamVideoConstraintsDto Video { get; set; }

        [JsonProperty("leaseSeconds")]
        public long LeaseSeconds { get; set; }
    }
}
