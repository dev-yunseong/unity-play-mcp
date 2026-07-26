using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class ArtelRequestDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// The sender's id for this request, echoed back on the ACTION_RESULT.
        /// </summary>
        /// <remarks>
        /// The server has always sent this; it was simply dropped here, which left
        /// the result with nothing to identify what it answered.
        /// </remarks>
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("actions")]
        public List<ActionRequestDto> Actions { get; set; } = new List<ActionRequestDto>();
    }
}
