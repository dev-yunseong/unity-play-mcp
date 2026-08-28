using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The screen every rect in the scene was measured against.
    /// </summary>
    /// <remarks>
    /// Without this a pixel rect says nothing: x 860 is the middle of a 1920 screen and the right
    /// third of a 1280 one. It also lets a reader recover a resolution-independent fraction, which
    /// reporting fractions alone would not let it undo.
    /// </remarks>
    public sealed class ScreenSizeDto
    {
        [JsonProperty("w")]
        public int W { get; set; }

        [JsonProperty("h")]
        public int H { get; set; }
    }
}
