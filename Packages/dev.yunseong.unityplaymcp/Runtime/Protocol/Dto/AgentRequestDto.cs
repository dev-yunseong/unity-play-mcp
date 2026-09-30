using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    internal sealed class AgentRequestDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// The sender's id for this request, echoed back on the ACTION_RESULT.
        /// </summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("actions")]
        public List<ActionRequestDto> Actions { get; set; } = new List<ActionRequestDto>();
    }
}
