using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class BlockTransformDto
    {
        [JsonProperty("world")]
        public WorldPositionDto World { get; set; }

        [JsonProperty("rect")]
        public ScreenRectDto Rect { get; set; }

        /// <summary>
        /// Whether <see cref="Rect"/> can be believed.
        /// </summary>
        /// <remarks>
        /// A block that projects normally but lands outside the frame reports false and keeps its
        /// measured rect, values outside 0..1 included, because how far off it sits is itself
        /// information. A block behind the camera or in a scene with no camera to project through
        /// reports false with a zeroed rect: there is nothing to measure and Unity's own numbers
        /// for the first case are mirrored rather than merely out of range.
        ///
        /// True does not mean visible: a block clipped by a mask, covered by another object, or
        /// drawn fully transparent still reports true.
        /// </remarks>
        [JsonProperty("onScreen")]
        public bool OnScreen { get; set; }
    }
}
