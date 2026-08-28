using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Pushes the lease deadline out. The viewer sends one every 10s; missing them is how the SDK
    /// learns that nobody is watching any more without being told.
    /// </summary>
    internal sealed class StreamRenewMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }
    }
}
