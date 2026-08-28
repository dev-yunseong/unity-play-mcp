using Artel.Diagnostics;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Artel.Tests.Diagnostics
{
    public sealed class FrameTimingMapperTests
    {
        private static FrameTimingBreakdown Breakdown(
            float? gpuMs = 9f,
            float? renderThreadMs = 6f,
            FrameTimingBottleneck bottleneck = FrameTimingBottleneck.MainThread)
        {
            return new FrameTimingBreakdown(
                frameCount: 42,
                cpuMs: 15f,
                cpuMainThreadMs: 14f,
                cpuRenderThreadMs: renderThreadMs,
                gpuMs: gpuMs,
                bottleneck: bottleneck);
        }

        [Test]
        public void ToDto_CarriesMillisecondsThroughUnchanged()
        {
            var dto = FrameTimingMapper.ToDto(Breakdown());

            // Unity가 ms로 주는 값이라 변환 지점이 없다. 초로 착각해 1000을 곱하면 천 배가 된다.
            Assert.AreEqual(42, dto.FrameCount);
            Assert.AreEqual(15f, dto.CpuMs.Value, 1e-3f);
            Assert.AreEqual(14f, dto.CpuMainThreadMs.Value, 1e-3f);
            Assert.AreEqual(6f, dto.CpuRenderThreadMs.Value, 1e-3f);
            Assert.AreEqual(9f, dto.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void ToDto_WritesTheBottleneckAsAStableWireValue()
        {
            Assert.AreEqual(
                "mainThread",
                FrameTimingMapper.ToDto(Breakdown(bottleneck: FrameTimingBottleneck.MainThread)).Bottleneck);
            Assert.AreEqual(
                "renderThread",
                FrameTimingMapper.ToDto(Breakdown(bottleneck: FrameTimingBottleneck.RenderThread)).Bottleneck);
            Assert.AreEqual(
                "gpu",
                FrameTimingMapper.ToDto(Breakdown(bottleneck: FrameTimingBottleneck.Gpu)).Bottleneck);
            Assert.AreEqual(
                "balanced",
                FrameTimingMapper.ToDto(Breakdown(bottleneck: FrameTimingBottleneck.Balanced)).Bottleneck);
            Assert.AreEqual(
                "unknown",
                FrameTimingMapper.ToDto(Breakdown(bottleneck: FrameTimingBottleneck.Unknown)).Bottleneck);
        }

        [Test]
        public void Serialized_LeavesOutTimingsThatWereNotCollected()
        {
            var dto = FrameTimingMapper.ToDto(Breakdown(gpuMs: null, renderThreadMs: null));

            var json = JsonConvert.SerializeObject(dto);

            // 0을 실으면 "GPU가 공짜"로 읽힌다. 필드가 없어야 미수집이다.
            StringAssert.DoesNotContain("gpuMs", json);
            StringAssert.DoesNotContain("cpuRenderThreadMs", json);
            StringAssert.Contains("cpuMainThreadMs", json);
        }

        [Test]
        public void Serialized_LeavesOutTheWholeGroupWhenNoTimingWasCollected()
        {
            var report = new PerformanceMessageDto { Type = "PERFORMANCE", Id = 1 };

            var json = JsonConvert.SerializeObject(report);

            // process와 같은 규칙이다. 그룹이 없으면 이 환경에서 재지 못했다는 뜻이다.
            StringAssert.DoesNotContain("frameTiming", json);
        }

        [Test]
        public void Serialized_KeepsFrameTimingSeparateFromFrameTimes()
        {
            var report = new PerformanceMessageDto
            {
                Type = "PERFORMANCE",
                Id = 1,
                FrameTimes = new FrameTimesDto { FrameCount = 60 },
                FrameTiming = FrameTimingMapper.ToDto(Breakdown())
            };

            var json = JsonConvert.SerializeObject(report);

            // frameTimes와 창이 달라 섞으면 안 되는 값이다. 형제 그룹으로 나가야 한다.
            StringAssert.Contains("\"frameTimes\":{\"frameCount\":60", json);
            StringAssert.Contains("\"frameTiming\":{\"frameCount\":42", json);
        }
    }
}
