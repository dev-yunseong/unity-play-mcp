using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class SceneBlockDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; } = true;

        [JsonProperty("transform", NullValueHandling = NullValueHandling.Ignore)]
        public BlockTransformDto Transform { get; set; }

        [JsonProperty("components")]
        public List<SceneComponentDto> Components { get; set; } = new List<SceneComponentDto>();

        [JsonProperty("children")]
        public List<SceneBlockDto> Children { get; set; } = new List<SceneBlockDto>();
    }
}
