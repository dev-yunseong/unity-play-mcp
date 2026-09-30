using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Protocol.Mapping;
using Newtonsoft.Json;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Diagnostics
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

            // Unity 가 ms 로 주므로 변환하지 않는다. 1000 을 곱하면 안 된다.
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

            // 0 을 실으면 "GPU 비용 없음" 으로 읽힌다. 미수집은 필드가 없어야 한다.
            StringAssert.DoesNotContain("gpuMs", json);
            StringAssert.DoesNotContain("cpuRenderThreadMs", json);
            StringAssert.Contains("cpuMainThreadMs", json);
        }

        [Test]
        public void Serialized_LeavesOutTheWholeGroupWhenNoTimingWasCollected()
        {
            var report = new PerformanceMessageDto { Type = "PERFORMANCE", Id = 1 };

            var json = JsonConvert.SerializeObject(report);

            // process 와 같은 규칙이다. 그룹이 없으면 이 환경에서 재지 못했다는 뜻이다.
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

            // frameTimes 와 측정 창이 달라 섞으면 안 되므로 별도 그룹으로 나간다.
            StringAssert.Contains("\"frameTimes\":{\"frameCount\":60", json);
            StringAssert.Contains("\"frameTiming\":{\"frameCount\":42", json);
        }
    }
}
