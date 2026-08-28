using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The `returnValue` of a successful `capture_screen`.
    /// </summary>
    /// <remarks>
    /// Only a pointer to the image. The bytes reach the agent over HTTP from storage, never over
    /// the QA WebSocket, because everything relayed on that socket is written to the QA log and
    /// republished over SSE.
    /// </remarks>
    internal sealed class CaptureResultDto
    {
        [JsonProperty("captureId")]
        public string CaptureId { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>After this the URL stops working, and the image can no longer be read.</summary>
        [JsonProperty("expiresAt")]
        public string ExpiresAt { get; set; }

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
    }
}
