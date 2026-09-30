using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Protocol.Mapping;
using UnityPlayMcp.Serialization;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Diagnostics
{
    /// <summary>
    /// 렌더 수치는 환경마다 다르고 batch mode 에서는 0 일 수 있으므로 불변식만 확인한다.
    /// </summary>
    public sealed class EditorRenderStatsReaderTests
    {
        [Test]
        public void TryRead_SucceedsInTheEditor()
        {
            // editor 에서만 도는 assembly 라 항상 참이다. 거짓이면 editorRender 가 보고에서 빠져도 알 수 없다.
            Assert.IsTrue(EditorRenderStatsReader.TryRead(out _));
        }

        [Test]
        public void TryRead_NeverReportsNegativeCounters()
        {
            Assert.IsTrue(EditorRenderStatsReader.TryRead(out var stats));

            // 음수는 UnityStats 의 UInt64 카운터를 int 로 좁히다 넘친 것이다.
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
        /// 필드가 없는 것이 가용성의 유일한 신호다. codec 기본값이 <c>NullValueHandling.Include</c> 라
        /// 속성에 <c>Ignore</c> 가 없으면 null 이 실린다.
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
