using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class EditTextComponentDto : SceneComponentDto
    {
        public override string Type => "editText";

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("placeholder", NullValueHandling = NullValueHandling.Ignore)]
        public string Placeholder { get; set; }

        [JsonProperty("interactable")]
        public bool Interactable { get; set; }
    }
}
