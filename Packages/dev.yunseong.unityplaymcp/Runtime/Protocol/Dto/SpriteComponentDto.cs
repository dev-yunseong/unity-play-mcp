using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class SpriteComponentDto : SceneComponentDto
    {
        public override string Type => "sprite";

        /// <summary>Absent when the renderer has no sprite assigned and so draws nothing.</summary>
        [JsonProperty("sprite", NullValueHandling = NullValueHandling.Ignore)]
        public string Sprite { get; set; }
    }
}
