using Artel.Diagnostics;
using Artel.Protocol.Mapping;
using NUnit.Framework;

namespace Artel.Tests.Diagnostics
{
    public sealed class FrameTimesMapperTests
    {
        [Test]
        public void ToDto_ConvertsSecondsToMillisecondsAndLeavesFpsAlone()
        {
            var statistics = new FrameTimeStatistics(
                frameCount: 60,
                sampledSeconds: 1f,
                meanSeconds: 0.016f,
                minSeconds: 0.010f,
                maxSeconds: 0.120f,
                percentile95Seconds: 0.020f,
                percentile99Seconds: 0.100f,
                onePercentLowFps: 8.5f,
                pointOnePercentLowFps: 8.3f,
                hitchCount: 2,
                hitchThresholdSeconds: 0.0333f,
                budgetSeconds: 0.0167f);

            var dto = FrameTimesMapper.ToDto(statistics);

            Assert.AreEqual(60, dto.FrameCount);
            Assert.AreEqual(1000f, dto.SampledMs, 1e-3f);
            Assert.AreEqual(16f, dto.MeanMs, 1e-3f);
            Assert.AreEqual(10f, dto.MinMs, 1e-3f);
            Assert.AreEqual(120f, dto.MaxMs, 1e-3f);
            Assert.AreEqual(20f, dto.P95Ms, 1e-3f);
            Assert.AreEqual(100f, dto.P99Ms, 1e-3f);
            Assert.AreEqual(33.3f, dto.HitchThresholdMs, 1e-3f);
            Assert.AreEqual(16.7f, dto.BudgetMs, 1e-3f);
            Assert.AreEqual(2, dto.HitchCount);

            // FPS는 이미 비율이라 단위 변환 대상이 아니다.
            Assert.AreEqual(8.5f, dto.OnePercentLowFps, 1e-3f);
            Assert.AreEqual(8.3f, dto.PointOnePercentLowFps, 1e-3f);
        }
    }
}
