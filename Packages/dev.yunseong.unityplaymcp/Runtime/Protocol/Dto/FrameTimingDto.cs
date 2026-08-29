using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 CPU·GPU 프레임타임 분해.
    /// </summary>
    /// <remarks>
    /// 프레임이 느릴 때 원인이 CPU인지 GPU인지 가르기 위한 값이다. 총 프레임타임만으로는
    /// "느리다"까지만 말할 수 있다.
    ///
    /// **<c>frameTimes</c>와 같은 창이 아니다.** Unity의 프레임 타이밍은 GPU가 프레임을 끝낸 뒤에야
    /// 확정되어 <c>GetLatestTimings</c>가 몇 프레임 지난 값을 준다. 이력의 길이도 Unity가 정하므로
    /// <c>frameCount</c>는 같은 구간의 <c>frameTimes.frameCount</c>보다 작은 것이 보통이다.
    /// 두 그룹의 값을 한 분포로 섞어 평균 내면 안 된다.
    ///
    /// 이 그룹 자체가 빠지면 미수집이다. Frame Timing Stats(Player Settings)가 꺼진 프로젝트에서는
    /// Unity가 아무 타이밍도 주지 않는데, SDK가 그 설정을 켤 수 없다. 0으로 채워 보내면 "안 쟀다"와
    /// "공짜로 그렸다"가 구분되지 않는다. 같은 이유로 개별 항목도 못 잰 것은 필드째 빠진다.
    ///
    /// 시간 값의 단위는 밀리초다. <c>frameTimes</c>와 눈금이 같다.
    /// </remarks>
    public sealed class FrameTimingDto
    {
        /// <summary>평균을 낸 프레임 수. 위에 적은 이유로 <c>frameTimes.frameCount</c>와 다르다.</summary>
        [JsonProperty("frameCount")]
        public int FrameCount { get; set; }

        /// <summary>프레임 하나를 만드는 데 든 CPU 전체 시간의 평균.</summary>
        [JsonProperty("cpuMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuMs { get; set; }

        /// <summary>메인 스레드 시간의 평균. 게임 로직과 렌더 커맨드 준비가 여기 들어간다.</summary>
        [JsonProperty("cpuMainThreadMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuMainThreadMs { get; set; }

        /// <summary>
        /// 렌더 스레드 시간의 평균. 멀티스레드 렌더링이 꺼진 구성에서는 값이 오지 않아 빠진다.
        /// </summary>
        [JsonProperty("cpuRenderThreadMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? CpuRenderThreadMs { get; set; }

        /// <summary>
        /// GPU 시간의 평균. 드라이버·플랫폼에 따라 타이머를 주지 않는 경우가 있고, 그때는 이 필드가
        /// 빠진다. 값이 없다고 GPU가 놀았다는 뜻이 아니다.
        /// </summary>
        [JsonProperty("gpuMs", NullValueHandling = NullValueHandling.Ignore)]
        public float? GpuMs { get; set; }

        /// <summary>
        /// 프레임을 가장 오래 붙잡은 구간. <c>mainThread</c>, <c>renderThread</c>, <c>gpu</c>,
        /// 1·2등이 엇비슷하면 <c>balanced</c>, 비교할 값이 없으면 <c>unknown</c>.
        ///
        /// 수집된 값끼리만 겨룬다. <c>gpuMs</c>가 빠진 보고의 분류는 CPU 쪽 두 값만 본 결과이므로,
        /// GPU 병목을 배제한 근거로 쓸 수 없다.
        /// </summary>
        [JsonProperty("bottleneck")]
        public string Bottleneck { get; set; }
    }
}
