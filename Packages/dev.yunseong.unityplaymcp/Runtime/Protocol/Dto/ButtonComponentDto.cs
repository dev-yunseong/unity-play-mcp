using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ButtonComponentDto : SceneComponentDto
    {
        public override string Type => "button";

        [JsonProperty("interactable")]
        public bool Interactable { get; set; }

        /// <summary>
        /// Left out entirely when nothing was collected, so a default scan keeps the shape it had.
        /// </summary>
        [JsonProperty("onClick", NullValueHandling = NullValueHandling.Ignore)]
        public List<ButtonClickHandlerDto> OnClick { get; set; }
    }
}
