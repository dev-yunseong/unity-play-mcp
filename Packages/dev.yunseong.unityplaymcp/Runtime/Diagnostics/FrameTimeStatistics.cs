namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 한 집계 구간의 프레임타임 분포.
    ///
    /// 평균만으로는 끊김이 드러나지 않으므로 백분위와 최악 프레임 값을 함께 담는다.
    /// </summary>
    internal readonly struct FrameTimeStatistics
    {
        public FrameTimeStatistics(
            int frameCount,
            float sampledSeconds,
            float meanSeconds,
            float minSeconds,
            float maxSeconds,
            float percentile95Seconds,
            float percentile99Seconds,
            float onePercentLowFps,
            float pointOnePercentLowFps,
            int hitchCount,
            float hitchThresholdSeconds,
            float budgetSeconds)
        {
            FrameCount = frameCount;
            SampledSeconds = sampledSeconds;
            MeanSeconds = meanSeconds;
            MinSeconds = minSeconds;
            MaxSeconds = maxSeconds;
            Percentile95Seconds = percentile95Seconds;
            Percentile99Seconds = percentile99Seconds;
            OnePercentLowFps = onePercentLowFps;
            PointOnePercentLowFps = pointOnePercentLowFps;
            HitchCount = hitchCount;
            HitchThresholdSeconds = hitchThresholdSeconds;
            BudgetSeconds = budgetSeconds;
        }

        /// <summary>집계에 들어간 프레임 수. 포커스를 잃은 프레임은 빠져 있다.</summary>
        public int FrameCount { get; }

        /// <summary>
        /// 집계된 프레임타임의 합.
        ///
        /// 집계 주기와 다르다. 포커스를 잃은 프레임은 빠지므로 5초 주기에 0.2초만 담길 수 있다.
        /// </summary>
        public float SampledSeconds { get; }

        public float MeanSeconds { get; }
        public float MinSeconds { get; }
        public float MaxSeconds { get; }
        public float Percentile95Seconds { get; }
        public float Percentile99Seconds { get; }

        /// <summary>최악 1% 프레임의 평균 프레임타임을 FPS로 환산한 값.</summary>
        public float OnePercentLowFps { get; }

        /// <summary>
        /// 최악 0.1% 프레임의 평균 프레임타임을 FPS로 환산한 값.
        ///
        /// 대상 프레임 수는 <c>max(1, ceil(FrameCount / 1000))</c>이다. 샘플이 1000 개 이하면
        /// <see cref="MaxSeconds"/>의 역수와 같다.
        /// </summary>
        public float PointOnePercentLowFps { get; }

        /// <summary><see cref="HitchThresholdSeconds"/>를 넘은 프레임 수.</summary>
        public int HitchCount { get; }

        public float HitchThresholdSeconds { get; }

        /// <summary>
        /// 이 구간에 적용한 프레임 예산. 같은 프레임타임도 예산에 따라 의미가 달라 함께 싣는다.
        /// </summary>
        public float BudgetSeconds { get; }
    }
}
