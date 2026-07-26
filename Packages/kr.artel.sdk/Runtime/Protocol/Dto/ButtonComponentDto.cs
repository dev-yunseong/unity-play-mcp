using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ButtonComponentDto : SceneComponentDto
    {
        public override string Type => "button";

        [JsonProperty("interactable")]
        public bool Interactable { get; set; }
    }
}
