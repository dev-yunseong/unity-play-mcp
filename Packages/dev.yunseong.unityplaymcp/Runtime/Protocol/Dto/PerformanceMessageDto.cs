using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 주기적으로 올리는 런타임 성능 보고.
    /// </summary>
    /// <remarks>
    /// 지표군마다 한 단계 아래로 묶어 서버가 군 단위로 읽게 한다.
    /// </remarks>
    public sealed class PerformanceMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("frameTimes")]
        public FrameTimesDto FrameTimes { get; set; }

        /// <summary>
        /// 프레임타임의 CPU·GPU 분해. 타이밍을 얻지 못하면 <c>null</c> 이라 필드째 빠진다.
        /// <c>frameTimes</c> 와 창이 다르다(<see cref="FrameTimingDto"/>).
        /// </summary>
        [JsonProperty(MetricGroupNames.FrameTiming, NullValueHandling = NullValueHandling.Ignore)]
        public FrameTimingDto FrameTiming { get; set; }

        /// <summary>
        /// 프로세스 CPU·메모리. 지원하지 않는 플랫폼이거나 첫 구간이면 <c>null</c> 이라 필드째 빠진다.
        /// </summary>
        [JsonProperty("process", NullValueHandling = NullValueHandling.Ignore)]
        public ProcessResourcesDto Process { get; set; }

        /// <summary>
        /// 에디터 Game view 의 렌더 통계. 에디터가 아니면 <c>null</c> 이라 필드째 빠진다.
        /// 집계가 아닌 순간값이다. <see cref="EditorRenderStatsDto"/> 참고.
        /// </summary>
        [JsonProperty(MetricGroupNames.EditorRender, NullValueHandling = NullValueHandling.Ignore)]
        public EditorRenderStatsDto EditorRender { get; set; }

        /// <summary>
        /// 이 구간의 실행 상태. 포커스와 배터리는 세션 중에 바뀌므로 보고마다 싣는다.
        /// </summary>
        [JsonProperty("status")]
        public RuntimeStatusDto Status { get; set; }
    }
}
