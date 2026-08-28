using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class GameStateMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("scene")]
        public SceneDto Scene { get; set; }
    }
}
