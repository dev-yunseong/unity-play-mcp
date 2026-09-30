namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 한 프레임의 Game view 렌더 통계. 구간 집계가 아닌 순간값이다.
    ///
    /// 에디터 값이라 Scene view·Inspector 프리뷰가 섞여 Standalone 수치와 다르다.
    /// </summary>
    internal readonly struct EditorRenderStats
    {
        public EditorRenderStats(
            int drawCalls,
            int batches,
            int setPassCalls,
            int triangles,
            int vertices,
            float mainThreadSeconds,
            float renderThreadSeconds)
        {
            DrawCalls = drawCalls;
            Batches = batches;
            SetPassCalls = setPassCalls;
            Triangles = triangles;
            Vertices = vertices;
            MainThreadSeconds = mainThreadSeconds;
            RenderThreadSeconds = renderThreadSeconds;
        }

        public int DrawCalls { get; }

        /// <summary>배칭 뒤 남은 배치 수. 드로우 콜 수 이하다.</summary>
        public int Batches { get; }

        /// <summary>셰이더 패스 전환 횟수. 드로우 콜보다 렌더 비용과 더 관련된다.</summary>
        public int SetPassCalls { get; }

        public int Triangles { get; }
        public int Vertices { get; }

        /// <summary>
        /// 메인 스레드 프레임 시간. <c>UnityStats.frameTime</c> 의 원시값이며 단위는 초다.
        /// </summary>
        public float MainThreadSeconds { get; }

        /// <summary>렌더 스레드 프레임 시간. <c>UnityStats.renderTime</c>의 원시값, 단위는 초.</summary>
        public float RenderThreadSeconds { get; }
    }
}
