using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 프레임타임 분포.
    /// </summary>
    /// <remarks>
    /// 평균만 실으면 hitch가 통계에 묻힌다. 평균 60fps여도 구간에 120ms 프레임이 몇 개 끼면
    /// 사용자는 끊긴다고 느끼므로, 백분위와 최악 프레임 기준값을 함께 보낸다.
    ///
    /// 시간 값의 단위는 밀리초다. 초 단위 float은 16.666668 같은 값이 되어 JSON에서 읽기 어렵고,
    /// 서버가 그대로 화면에 쓰기에도 ms가 관례다.
    /// </remarks>
    public sealed class FrameTimesDto
    {
        /// <summary>이 구간에 집계된 프레임 수. 포커스를 잃은 프레임은 빠져 있다.</summary>
        [JsonProperty("frameCount")]
        public int FrameCount { get; set; }

        /// <summary>
        /// 집계된 프레임타임의 합. 전송 주기와 다를 수 있다. 구간의 일부만 포커스를 가졌다면
        /// 1초 주기에 200ms만 담기고, 이 값이 없으면 커버리지를 알 수 없다.
        /// </summary>
        [JsonProperty("sampledMs")]
        public float SampledMs { get; set; }

        [JsonProperty("meanMs")]
        public float MeanMs { get; set; }

        [JsonProperty("minMs")]
        public float MinMs { get; set; }

        [JsonProperty("maxMs")]
        public float MaxMs { get; set; }

        [JsonProperty("p95Ms")]
        public float P95Ms { get; set; }

        [JsonProperty("p99Ms")]
        public float P99Ms { get; set; }

        /// <summary>최악 1% 프레임의 평균을 FPS로 환산한 값.</summary>
        [JsonProperty("onePercentLowFps")]
        public float OnePercentLowFps { get; set; }

        /// <summary>
        /// 최악 0.1% 프레임의 평균을 FPS로 환산한 값. 대상 프레임 수는
        /// <c>max(1, ceil(frameCount / 1000))</c>이라, 1초 창(약 60샘플)에서는 대상이 한 프레임으로
        /// 떨어져 <c>maxMs</c>의 역수와 같아진다. 창 길이의 한계이지 계산 오류가 아니다.
        /// </summary>
        [JsonProperty("pointOnePercentLowFps")]
        public float PointOnePercentLowFps { get; set; }

        /// <summary><c>hitchThresholdMs</c>를 넘은 프레임 수.</summary>
        [JsonProperty("hitchCount")]
        public int HitchCount { get; set; }

        [JsonProperty("hitchThresholdMs")]
        public float HitchThresholdMs { get; set; }

        /// <summary>
        /// 이 구간에 적용한 프레임 예산. 같은 33ms라도 30fps 캡이 걸린 빌드에서는 정상이고
        /// 144Hz 모니터에서는 결함이므로, 해석 근거로 함께 싣는다.
        /// </summary>
        [JsonProperty("budgetMs")]
        public float BudgetMs { get; set; }
    }
}
