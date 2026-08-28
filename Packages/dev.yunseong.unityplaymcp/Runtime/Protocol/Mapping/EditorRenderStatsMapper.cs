using Artel.Diagnostics;
using Artel.Protocol.Dto;

namespace Artel.Protocol.Mapping
{
    /// <summary>
    /// 렌더 통계를 전송용 DTO로 옮긴다. 카운터는 그대로 가고, 초를 밀리초로 바꾸는 지점만
    /// 여기 한 곳으로 모은다. <c>UnityStats.frameTime</c>·<c>renderTime</c>은 초 단위이고
    /// Game view Stats 창도 표시할 때 1000을 곱한다.
    /// </summary>
    internal static class EditorRenderStatsMapper
    {
        private const float MillisecondsPerSecond = 1000f;

        public static EditorRenderStatsDto ToDto(EditorRenderStats stats)
        {
            return new EditorRenderStatsDto
            {
                DrawCalls = stats.DrawCalls,
                Batches = stats.Batches,
                SetPassCalls = stats.SetPassCalls,
                Triangles = stats.Triangles,
                Vertices = stats.Vertices,
                MainThreadMs = stats.MainThreadSeconds * MillisecondsPerSecond,
                RenderThreadMs = stats.RenderThreadSeconds * MillisecondsPerSecond
            };
        }
    }
}
