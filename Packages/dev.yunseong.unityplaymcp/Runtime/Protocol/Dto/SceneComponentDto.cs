using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public abstract class SceneComponentDto
    {
        [JsonProperty("type")]
        public abstract string Type { get; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("states")]
        public List<StateDto> States { get; set; } = new List<StateDto>();

        [JsonProperty("actions")]
        public List<ActionInvocationDto> Actions { get; set; } = new List<ActionInvocationDto>();
    }
}
