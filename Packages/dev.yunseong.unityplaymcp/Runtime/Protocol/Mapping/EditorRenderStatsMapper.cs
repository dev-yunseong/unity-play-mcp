using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Protocol.Mapping
{
    /// <summary>
    /// 렌더 통계를 전송용 DTO 로 옮긴다. <c>UnityStats</c> 의 초 단위 시간을 밀리초로 바꾼다.
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
