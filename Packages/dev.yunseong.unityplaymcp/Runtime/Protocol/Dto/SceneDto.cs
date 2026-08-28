using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class SceneDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// The screen the blocks' rects were measured against. Once per scene rather than once per
        /// block, because one scan measures against one screen.
        /// </summary>
        [JsonProperty("screen", NullValueHandling = NullValueHandling.Ignore)]
        public ScreenSizeDto Screen { get; set; }

        [JsonProperty("children")]
        public List<SceneBlockDto> Children { get; set; } = new List<SceneBlockDto>();
    }
}
