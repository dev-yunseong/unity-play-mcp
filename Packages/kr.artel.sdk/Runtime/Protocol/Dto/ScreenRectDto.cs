using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The area a block covers on screen, normalized to 0..1 with the origin at the top left.
    /// </summary>
    /// <remarks>
    /// <see cref="X"/> and <see cref="Y"/> are the top-left corner rather than the centre.
    /// <see cref="W"/> is divided by the screen's width and <see cref="H"/> by its height, so equal
    /// values do not describe a square.
    /// </remarks>
    public sealed class ScreenRectDto
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("w")]
        public float W { get; set; }

        [JsonProperty("h")]
        public float H { get; set; }
    }
}
