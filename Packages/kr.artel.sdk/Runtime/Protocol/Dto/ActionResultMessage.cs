using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ActionResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("results")]
        public List<ActionResultDto> Results { get; set; } = new List<ActionResultDto>();
    }
}
