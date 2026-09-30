using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 CPU·GPU 프레임타임 분해.
    /// </summary>
    /// <remarks>
    /// 느린 프레임의 원인이 CPU 인지 GPU 인지 가른다. 단위는 밀리초다.
    ///
    /// <c>frameTimes</c> 와 같은 창이 아니다. <c>GetLatestTimings</c> 는 몇 프레임 지난 값을 주고
    /// 이력 길이도 Unity 가 정한다. 두 그룹을 한 분포로 섞으면 안 된다.
    ///
    /// Frame Timing Stats(Player Settings)가 꺼져 있으면 이 그룹이 빠진다. 못 잰 개별 항목도
    /// 0 대신 필드째 빠진다.
    /// </remarks>
    public sealed class FrameTimingDto
    {
        /// <summary>평균을 낸 프레임 수. <c>frameTimes.frameCount</c> 와 다르다.</summary>
        [JsonProperty("frameCount")]
        public int FrameCount { get; set; }

        /// <summary>프레임 하나를 만드는 데 든 CPU 전체 시간의 평균.</summary>
        [JsonProperty("cpuMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuMs { get; set; }

        /// <summary>메인 스레드 시간의 평균. 게임 로직과 렌더 커맨드 준비가 여기 들어간다.</summary>
        [JsonProperty("cpuMainThreadMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuMainThreadMs { get; set; }

        /// <summary>
        /// 렌더 스레드 시간의 평균. 멀티스레드 렌더링이 꺼져 있으면 빠진다.
        /// </summary>
        [JsonProperty("cpuRenderThreadMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuRenderThreadMs { get; set; }

        /// <summary>
        /// GPU 시간의 평균. 드라이버·플랫폼이 타이머를 주지 않으면 빠진다.
        /// </summary>
        [JsonProperty("gpuMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? GpuMs { get; set; }

        /// <summary>
        /// 가장 오래 걸린 구간. <c>mainThread</c>, <c>renderThread</c>, <c>gpu</c>,
        /// 1·2등이 비슷하면 <c>balanced</c>, 비교할 값이 없으면 <c>unknown</c>.
        ///
        /// 수집된 값끼리만 비교하므로 <c>gpuMs</c> 가 없으면 GPU 병목을 배제할 수 없다.
        /// </summary>
        [JsonProperty("bottleneck")]
        public string Bottleneck { get; set; }
    }
}
