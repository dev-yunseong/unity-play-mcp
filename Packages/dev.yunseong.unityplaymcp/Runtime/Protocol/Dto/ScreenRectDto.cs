using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The area a block covers on screen, in pixels, with the origin at the top left.
    /// </summary>
    /// <remarks>
    /// <see cref="X"/> and <see cref="Y"/> are the top-left corner rather than the centre. The
    /// pixels are the game's own screen, which the scene reports alongside as <c>screen</c>; a
    /// reader working against a downscaled video frame scales by the ratio between the two.
    /// </remarks>
    public sealed class ScreenRectDto
    {
        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("w")]
        public int W { get; set; }

        [JsonProperty("h")]
        public int H { get; set; }
    }
}
