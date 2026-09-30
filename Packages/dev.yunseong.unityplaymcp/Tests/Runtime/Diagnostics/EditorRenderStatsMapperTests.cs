using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Mapping;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Diagnostics
{
    public sealed class EditorRenderStatsMapperTests
    {
        private static EditorRenderStats Stats(float mainThreadSeconds, float renderThreadSeconds)
        {
            return new EditorRenderStats(
                drawCalls: 120,
                batches: 84,
                setPassCalls: 31,
                triangles: 45000,
                vertices: 27000,
                mainThreadSeconds: mainThreadSeconds,
                renderThreadSeconds: renderThreadSeconds);
        }

        [Test]
        public void ToDto_ConvertsSecondsToMillisecondsAndLeavesCountersAlone()
        {
            var dto = EditorRenderStatsMapper.ToDto(Stats(0.016f, 0.004f));

            // UnityStats.frameTime·renderTime 은 초 단위다. Stats 창도 1000 을 곱해 표시한다.
            Assert.AreEqual(16f, dto.MainThreadMs, 1e-3f);
            Assert.AreEqual(4f, dto.RenderThreadMs, 1e-3f);

            // 카운터는 단위가 없어 변환하지 않는다.
            Assert.AreEqual(120, dto.DrawCalls);
            Assert.AreEqual(84, dto.Batches);
            Assert.AreEqual(31, dto.SetPassCalls);
            Assert.AreEqual(45000, dto.Triangles);
            Assert.AreEqual(27000, dto.Vertices);
        }

        /// <summary>
        /// 0초 → 0ms 로는 배율 실수를 잡지 못한다. 배율을 틀리면 60fps 프레임이 0.016ms 로 올라가 정상으로 읽히므로 배율을 확인한다.
        /// </summary>
        [Test]
        public void ToDto_ScalesTimesByExactlyOneThousand()
        {
            var dto = EditorRenderStatsMapper.ToDto(Stats(1f, 0.5f));

            Assert.AreEqual(1000f, dto.MainThreadMs, 1e-3f);
            Assert.AreEqual(500f, dto.RenderThreadMs, 1e-3f);
        }
    }
}
