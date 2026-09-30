using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Protocol.Mapping
{
    /// <summary>
    /// 프레임 분포를 전송용 DTO 로 옮기며 초를 밀리초로 바꾼다.
    /// </summary>
    internal static class FrameTimesMapper
    {
        private const float MillisecondsPerSecond = 1000f;

        public static FrameTimesDto ToDto(FrameTimeStatistics statistics)
        {
            return new FrameTimesDto
            {
                FrameCount = statistics.FrameCount,
                SampledMs = ToMilliseconds(statistics.SampledSeconds),
                MeanMs = ToMilliseconds(statistics.MeanSeconds),
                MinMs = ToMilliseconds(statistics.MinSeconds),
                MaxMs = ToMilliseconds(statistics.MaxSeconds),
                P95Ms = ToMilliseconds(statistics.Percentile95Seconds),
                P99Ms = ToMilliseconds(statistics.Percentile99Seconds),
                OnePercentLowFps = statistics.OnePercentLowFps,
                PointOnePercentLowFps = statistics.PointOnePercentLowFps,
                HitchCount = statistics.HitchCount,
                HitchThresholdMs = ToMilliseconds(statistics.HitchThresholdSeconds),
                BudgetMs = ToMilliseconds(statistics.BudgetSeconds)
            };
        }

        private static float ToMilliseconds(float seconds)
        {
            return seconds * MillisecondsPerSecond;
        }
    }
}
