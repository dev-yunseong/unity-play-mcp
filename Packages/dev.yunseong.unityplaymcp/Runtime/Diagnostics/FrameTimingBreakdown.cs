namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 한 집계 구간의 CPU·GPU 프레임타임 분해와 병목 분류. 단위는 밀리초다.
    ///
    /// 수집되지 않은 항목은 0 대신 <c>null</c> 로 두어 보고에서 뺀다. 0 은 비용이 없다고 읽힌다.
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
        /// 평균을 낸 프레임 수. 이력 길이를 Unity 가 정하므로 <c>FrameTimeStatistics.FrameCount</c> 보다 대체로 적다.
        /// </summary>
        public int FrameCount { get; }

        public float? CpuMs { get; }
        public float? CpuMainThreadMs { get; }
        public float? CpuRenderThreadMs { get; }
        public float? GpuMs { get; }

        public FrameTimingBottleneck Bottleneck { get; }
    }
}
