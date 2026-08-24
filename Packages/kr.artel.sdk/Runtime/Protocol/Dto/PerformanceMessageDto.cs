using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 주기적으로 올리는 런타임 성능 보고.
    /// </summary>
    /// <remarks>
    /// 지표군마다 최상위에 펼치지 않고 한 단계 아래로 묶는다. 종류가 늘어도 최상위가 평평하게
    /// 커지지 않고, 서버가 군 단위로 골라 읽을 수 있다.
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
        /// 프레임타임의 CPU·GPU 분해. Frame Timing Stats가 꺼져 있거나 플랫폼이 타이밍을 주지
        /// 않으면 <c>null</c>이라 필드 자체가 빠진다.
        ///
        /// <c>frameTimes</c>를 쪼갠 값이 아니다. 창이 서로 달라서(자세한 이유는
        /// <see cref="FrameTimingDto"/>) 두 그룹을 하나의 분포처럼 합치면 안 된다.
        /// </summary>
        [JsonProperty(MetricGroupNames.FrameTiming, NullValueHandling = NullValueHandling.Ignore)]
        public FrameTimingDto FrameTiming { get; set; }

        /// <summary>
        /// 프로세스 CPU·메모리. 읽을 수 없는 플랫폼이거나 아직 비교할 이전 판독이 없으면
        /// <c>null</c>이라 필드 자체가 빠진다. 0을 채워 보내면 "안 재는 환경"과 "정말 놀고 있는
        /// 프로세스"가 구분되지 않는다.
        /// </summary>
        [JsonProperty("process", NullValueHandling = NullValueHandling.Ignore)]
        public ProcessResourcesDto Process { get; set; }

        /// <summary>
        /// 에디터 Game view의 렌더 통계. 에디터가 아니면 <c>null</c>이라 필드 자체가 빠진다.
        /// 가용성은 별도 플래그가 아니라 이 부재로만 알린다.
        ///
        /// 다른 지표군과 달리 이 창의 집계가 아닌 순간값이고, Scene view 렌더가 섞여 있어
        /// Standalone 수치와 나란히 놓을 수 없다. <see cref="EditorRenderStatsDto"/> 참고.
        /// </summary>
        [JsonProperty(MetricGroupNames.EditorRender, NullValueHandling = NullValueHandling.Ignore)]
        public EditorRenderStatsDto EditorRender { get; set; }

        /// <summary>
        /// 이 구간의 실행 상태. 포커스와 배터리는 세션 중에 바뀌므로 보고마다 싣는다.
        /// 배터리가 방전 중이면 노트북 스로틀링이 걸렸을 수 있어, 이 값 없이는 느려진 원인을
        /// 코드에서 찾게 된다.
        /// </summary>
        [JsonProperty("status")]
        public RuntimeStatusDto Status { get; set; }
    }
}
