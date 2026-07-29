using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// Asks orchestration to sign an upload. The image itself never goes through that server.
    /// </summary>
    internal sealed class CaptureTicketRequestDto
    {
        [JsonProperty("instanceKey")]
        public string InstanceKey { get; set; }

        [JsonProperty("contentType")]
        public string ContentType { get; set; }

        [JsonProperty("contentLength")]
        public long ContentLength { get; set; }

        [JsonProperty("targetId", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetId { get; set; }
    }

    internal sealed class CaptureTicketResponseDto
    {
        [JsonProperty("captureId")]
        public string CaptureId { get; set; }

        [JsonProperty("uploadUrl")]
        public string UploadUrl { get; set; }

        /// <summary>Headers the signature covers. A PUT without them is rejected by storage.</summary>
        [JsonProperty("requiredHeaders")]
        public Dictionary<string, string> RequiredHeaders { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("downloadExpiresAt")]
        public string DownloadExpiresAt { get; set; }
    }
}
