using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ImageComponentDto : SceneComponentDto
    {
        public override string Type => "image";

        /// <summary>
        /// Absent when the Image has no sprite assigned, which is how a flat-colour panel or an
        /// invisible raycast catcher is built.
        /// </summary>
        [JsonProperty("sprite", NullValueHandling = NullValueHandling.Ignore)]
        public string Sprite { get; set; }
    }
}
