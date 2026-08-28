using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class AllScenesMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("scenes")]
        public List<ScannedSceneDto> Scenes { get; set; } = new List<ScannedSceneDto>();
    }
}
