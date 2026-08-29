using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ScannedSceneDto
    {
        [JsonProperty("buildIndex")]
        public int BuildIndex { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("scene")]
        public SceneDto Scene { get; set; }
    }
}
