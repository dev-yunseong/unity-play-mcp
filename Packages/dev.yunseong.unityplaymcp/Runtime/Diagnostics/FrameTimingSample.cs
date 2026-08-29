namespace Artel.Diagnostics
{
    /// <summary>
    /// 한 프레임의 CPU·GPU 분해 판독값.
    ///
    /// 단위는 밀리초다. Unity의 <c>FrameTiming</c>이 이미 ms로 주므로 이 경계에서 환산하지 않는다.
    /// 초 단위로 모으는 <c>FrameTimeStatistics</c>와 헷갈리지 말 것.
    ///
    /// 값이 0이나 음수로 오는 항목이 있다. 드라이버·플랫폼에 따라 GPU 타이밍이 채워지지 않고,
    /// 렌더 스레드가 없는 구성에서는 렌더 스레드 시간이 0이다. 집계 쪽에서 미수집으로 다룬다.
    /// </summary>
    internal readonly struct FrameTimingSample
    {
        public FrameTimingSample(
            double cpuMs,
            double cpuMainThreadMs,
            double cpuRenderThreadMs,
            double gpuMs)
        {
            CpuMs = cpuMs;
            CpuMainThreadMs = cpuMainThreadMs;
            CpuRenderThreadMs = cpuRenderThreadMs;
            GpuMs = gpuMs;
        }

        /// <summary>프레임 하나를 만드는 데 든 CPU 전체 시간.</summary>
        public double CpuMs { get; }

        /// <summary>메인 스레드가 이 프레임에 쓴 시간.</summary>
        public double CpuMainThreadMs { get; }

        /// <summary>렌더 스레드가 이 프레임에 쓴 시간. 멀티스레드 렌더링이 꺼져 있으면 0이다.</summary>
        public double CpuRenderThreadMs { get; }

        /// <summary>GPU가 이 프레임에 쓴 시간. 드라이버가 타이머를 주지 않으면 0이다.</summary>
        public double GpuMs { get; }
    }
}
