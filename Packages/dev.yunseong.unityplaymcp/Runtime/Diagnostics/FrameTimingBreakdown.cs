namespace Artel.Diagnostics
{
    /// <summary>
    /// 한 집계 구간의 CPU·GPU 프레임타임 분해와 병목 분류.
    ///
    /// 시간 값의 단위는 밀리초이고, 항목마다 따로 없을 수 있다. 드라이버가 GPU 타이머를 주지
    /// 않거나 렌더 스레드가 없는 구성에서는 해당 값이 0으로 오는데, 0을 그대로 실으면 "공짜로
    /// 그렸다"로 읽힌다. 그래서 없는 항목은 <c>null</c>로 남겨 보고에서 통째로 뺀다.
    /// </summary>
    internal readonly struct FrameTimingBreakdown
    {
        public FrameTimingBreakdown(
            int frameCount,
            float? cpuMs,
            float? cpuMainThreadMs,
            float? cpuRenderThreadMs,
            float? gpuMs,
            FrameTimingBottleneck bottleneck)
        {
            FrameCount = frameCount;
            CpuMs = cpuMs;
            CpuMainThreadMs = cpuMainThreadMs;
            CpuRenderThreadMs = cpuRenderThreadMs;
            GpuMs = gpuMs;
            Bottleneck = bottleneck;
        }

        /// <summary>
        /// 평균을 낸 프레임 수. 프레임 타이밍 이력은 Unity가 들고 있고 그 길이를 SDK가 정할 수
        /// 없어서, 같은 구간의 <c>FrameTimeStatistics.FrameCount</c>보다 대체로 적다.
        /// </summary>
        public int FrameCount { get; }

        public float? CpuMs { get; }
        public float? CpuMainThreadMs { get; }
        public float? CpuRenderThreadMs { get; }
        public float? GpuMs { get; }

        public FrameTimingBottleneck Bottleneck { get; }
    }
}
