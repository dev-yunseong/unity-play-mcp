using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 주기적으로 올리는 런타임 성능 보고. 지금은 프레임 지표만 싣는다.
    /// </summary>
    /// <remarks>
    /// CPU·메모리(ARTEL-344)와 디바이스 컨텍스트(ARTEL-345)가 같은 메시지에 필드로 붙는다.
    /// 그래서 프레임 지표를 최상위에 펼치지 않고 <c>frameTimes</c> 아래에 묶어 둔다.
    /// </remarks>
    public sealed class PerformanceMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("frameTimes")]
        public FrameTimesDto FrameTimes { get; set; }
    }
}
