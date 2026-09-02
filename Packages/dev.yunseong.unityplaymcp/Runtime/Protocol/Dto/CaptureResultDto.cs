using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// The `returnValue` of a successful `capture_screen`.
    /// </summary>
    internal sealed class CaptureResultDto
    {
        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        /// <summary>Absent for a whole-screen capture.</summary>
        [JsonProperty("targetId", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetId { get; set; }

        /// <summary>
        /// True when the screen cut the requested element short.
        /// </summary>
        /// <remarks>
        /// Reported rather than failed: an element hanging off the edge of the screen is itself a
        /// finding, and the visible part is still evidence for it.
        /// </remarks>
        [JsonProperty("clipped")]
        public bool Clipped { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }
    }
}
