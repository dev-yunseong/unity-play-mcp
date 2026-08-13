namespace Artel.Diagnostics
{
    /// <summary>
    /// 한 시점의 Game view 렌더 통계.
    ///
    /// 구간 집계가 아니라 읽은 그 프레임의 순간값이다. <c>FrameTimeStatistics</c>처럼 창 안의
    /// 모든 프레임을 접은 값이 아니므로, 평균이나 백분위로 읽으면 안 된다.
    ///
    /// 에디터가 렌더한 값이라 Scene view·Inspector 프리뷰가 함께 섞인다. 같은 씬을 Standalone에서
    /// 재도 수치가 맞지 않는 것이 정상이다.
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

        /// <summary>배칭 뒤 남은 배치 수. 드로우 콜보다 크거나 같을 이유가 없다.</summary>
        public int Batches { get; }

        /// <summary>셰이더 패스 전환 횟수. 드로우 콜보다 이쪽이 렌더 비용에 더 붙는다.</summary>
        public int SetPassCalls { get; }

        public int Triangles { get; }
        public int Vertices { get; }

        /// <summary>
        /// 메인 스레드 프레임 시간. <c>UnityStats.frameTime</c>의 원시값이며 단위는 초다.
        /// Game view Stats 창은 이 값에 1000을 곱해 ms로 보여 준다.
        /// </summary>
        public float MainThreadSeconds { get; }

        /// <summary>렌더 스레드 프레임 시간. <c>UnityStats.renderTime</c>의 원시값, 단위는 초.</summary>
        public float RenderThreadSeconds { get; }
    }
}
