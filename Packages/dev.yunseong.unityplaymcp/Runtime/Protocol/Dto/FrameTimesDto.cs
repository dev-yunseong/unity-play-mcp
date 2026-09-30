using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 프레임타임 분포.
    /// </summary>
    /// <remarks>
    /// 평균만으로는 hitch 가 묻히므로 백분위와 최악 프레임 값을 함께 보낸다. 단위는 밀리초다.
    /// </remarks>
    public sealed class FrameTimesDto
    {
        /// <summary>이 구간에 집계된 프레임 수. 포커스를 잃은 프레임은 빠져 있다.</summary>
        [JsonProperty("frameCount")]
        public int FrameCount { get; set; }

        /// <summary>
        /// 집계된 프레임타임의 합. 포커스를 잃은 프레임이 빠지므로 전송 주기보다 짧을 수 있다.
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
        /// 최악 0.1% 프레임의 평균을 FPS 로 환산한 값. 대상 프레임 수는
        /// <c>max(1, ceil(frameCount / 1000))</c> 이라 짧은 창에서는 <c>maxMs</c> 의 역수와 같다.
        /// </summary>
        [JsonProperty("pointOnePercentLowFps")]
        public float PointOnePercentLowFps { get; set; }

        /// <summary><c>hitchThresholdMs</c>를 넘은 프레임 수.</summary>
        [JsonProperty("hitchCount")]
        public int HitchCount { get; set; }

        [JsonProperty("hitchThresholdMs")]
        public float HitchThresholdMs { get; set; }

        /// <summary>
        /// 이 구간에 적용한 프레임 예산. 같은 프레임타임도 예산에 따라 의미가 달라 함께 싣는다.
        /// </summary>
        [JsonProperty("budgetMs")]
        public float BudgetMs { get; set; }
    }
}
