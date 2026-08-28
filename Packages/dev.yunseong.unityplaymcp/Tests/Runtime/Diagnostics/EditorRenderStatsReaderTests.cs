using Artel.Diagnostics;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using NUnit.Framework;

namespace Artel.Tests.Diagnostics
{
    /// <summary>
    /// 실제 렌더 수치를 단정하면 실행 환경마다 결과가 갈린다. 배치모드에서는 그리는 것이 거의
    /// 없어 값이 0일 수도 있으므로, 어느 환경에서나 성립해야 하는 불변식만 확인한다.
    /// </summary>
    public sealed class EditorRenderStatsReaderTests
    {
        [Test]
        public void TryRead_SucceedsInTheEditor()
        {
            // 이 어셈블리는 에디터에서만 돌기 때문에 항상 참이어야 한다. 거짓이면 보고에서
            // editorRender가 통째로 사라져도 아무도 눈치채지 못한다.
            Assert.IsTrue(EditorRenderStatsReader.TryRead(out _));
        }

        [Test]
        public void TryRead_NeverReportsNegativeCounters()
        {
            Assert.IsTrue(EditorRenderStatsReader.TryRead(out var stats));

            // 음수는 UnityStats의 UInt64 카운터를 int로 좁히다 넘친 흔적이다.
            Assert.GreaterOrEqual(stats.DrawCalls, 0);
            Assert.GreaterOrEqual(stats.Batches, 0);
            Assert.GreaterOrEqual(stats.SetPassCalls, 0);
            Assert.GreaterOrEqual(stats.Triangles, 0);
            Assert.GreaterOrEqual(stats.Vertices, 0);
            Assert.GreaterOrEqual(stats.MainThreadSeconds, 0f);
            Assert.GreaterOrEqual(stats.RenderThreadSeconds, 0f);
        }

        // --- wire shape ---

        /// <summary>
        /// 가용성을 알리는 유일한 신호가 필드의 부재다. 코덱의 기본값이
        /// <c>NullValueHandling.Include</c>라 속성의 <c>Ignore</c>가 없으면 null이 그대로 실린다.
        /// </summary>
        [Test]
        public void Serialize_OmitsTheRenderGroupWhenItWasNotRead()
        {
            var json = new NewtonsoftJsonCodec().Serialize(new PerformanceMessageDto
            {
                Type = "PERFORMANCE",
                Id = 1
            });

            Assert.That(json, Does.Not.Contain("editorRender"));
        }

        [Test]
        public void Serialize_CarriesTheRenderGroupWhenItWasRead()
        {
            Assert.IsTrue(EditorRenderStatsReader.TryRead(out var stats));

            var json = new NewtonsoftJsonCodec().Serialize(new PerformanceMessageDto
            {
                Type = "PERFORMANCE",
                Id = 1,
                EditorRender = EditorRenderStatsMapper.ToDto(stats)
            });

            Assert.That(json, Does.Contain("editorRender"));
            Assert.That(json, Does.Contain("setPassCalls"));
            Assert.That(json, Does.Contain("mainThreadMs"));
        }
    }
}
