using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class ArtelRequestDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("actions")]
        public List<ActionRequestDto> Actions { get; set; } = new List<ActionRequestDto>();
    }
}
