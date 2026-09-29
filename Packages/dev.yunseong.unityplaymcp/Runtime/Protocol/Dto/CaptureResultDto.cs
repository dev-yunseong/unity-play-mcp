using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// The `returnValue` of a successful `capture_screen`.
    /// </summary>
    internal sealed class CaptureResultDto
    {
        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        /// <summary>Absent for a whole-screen capture.</summary>
        [JsonProperty("targetId", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetId { get; set; }

        /// <summary>
        /// True when the screen cut the requested element short.
        /// </summary>
        /// <remarks>
        /// Reported rather than failed: an element hanging off the edge of the screen is itself a
        /// finding, and the visible part is still evidence for it.
        /// </remarks>
        [JsonProperty("clipped")]
        public bool Clipped { get; set; }

        /// <summary>캡처한 순간의 <c>Screen.width</c>/<c>Screen.height</c>. <c>move_mouse</c> 와 scene rect 가 쓰는 공간이다.</summary>
        [JsonProperty("screen", NullValueHandling = NullValueHandling.Ignore)]
        public CaptureScreenSizeDto Screen { get; set; }

        /// <summary>이미지가 실제로 담은 화면 영역. 좌상단 기준 화면 픽셀이다.</summary>
        /// <remarks>대상 crop 이면 padding 만큼 대상보다 크고, 잘렸으면 잘린 뒤의 영역이다. 이미지에 여백 띠는 없다.</remarks>
        [JsonProperty("region", NullValueHandling = NullValueHandling.Ignore)]
        public CaptureAreaDto Region { get; set; }

        /// <summary>잘리기 전에 청한 영역. <see cref="Clipped"/> 일 때만 싣는다.</summary>
        [JsonProperty("requestedRegion", NullValueHandling = NullValueHandling.Ignore)]
        public CaptureAreaDto RequestedRegion { get; set; }

        /// <summary>화면 픽셀 하나가 이미지에서 차지하는 픽셀 수. 축마다 따로 잰다 — 줄인 크기를 정수로 반올림하기 때문이다.</summary>
        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)]
        public CaptureScaleDto Scale { get; set; }

        /// <summary>back buffer 를 읽은 프레임.</summary>
        [JsonProperty("frame", NullValueHandling = NullValueHandling.Ignore)]
        public int? Frame { get; set; }

        /// <summary>그 순간의 활성 씬.</summary>
        [JsonProperty("scene", NullValueHandling = NullValueHandling.Ignore)]
        public string Scene { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }
    }

    internal sealed class CaptureScreenSizeDto
    {
        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }

    internal sealed class CaptureAreaDto
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("width")]
        public float Width { get; set; }

        [JsonProperty("height")]
        public float Height { get; set; }
    }

    internal sealed class CaptureScaleDto
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }
    }
}
