using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class TextComponentDto : SceneComponentDto
    {
        public override string Type => "text";

        [JsonProperty("content")]
        public string Content { get; set; }
    }
}
