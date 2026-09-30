namespace UnityPlayMcp.Protocol
{
    /// <summary>
    /// 성능 보고가 싣는 지표군의 와이어 이름.
    /// </summary>
    /// <remarks>
    /// 서버가 정한 계약이다. 서버는 모르는 최상위 필드를 모두 지표군으로 받으므로 철자가 틀리면
    /// 오류 없이 새 군이 생긴다.
    ///
    /// <see cref="Dto.PerformanceMessageDto"/> 의 직렬화 이름과 <see cref="Collected"/> 가 같은 상수를
    /// 봐야 한다. 어긋나면 서버는 그 군을 지원하지 않는다고 답한다.
    /// </remarks>
    internal static class MetricGroupNames
    {
        /// <summary>CPU·GPU 프레임타임 분해.</summary>
        public const string FrameTiming = "frameTiming";

        /// <summary>에디터 Game view의 렌더 통계.</summary>
        public const string EditorRender = "editorRender";

        /// <summary>
        /// 이 SDK 빌드가 수집을 <em>시도하는</em> 군 전부.
        ///
        /// 플랫폼과 무관하다. 실제 수집 여부는 보고에 그 군이 실렸는지로 알 수 있다.
        /// 그래야 구버전 SDK 와 Standalone 의 에디터 전용 군을 구분할 수 있다.
        ///
        /// 아직 수집하지 않는 군은 적지 않는다. 적으면 서버가 재려 했으나 못 쟀다고 답한다.
        /// </summary>
        private static readonly string[] CollectedGroups = { FrameTiming, EditorRender };

        /// <summary>
        /// <see cref="CollectedGroups"/> 의 복사본. 원본 배열을 넘기면 호출자가 바꿀 수 있다.
        /// </summary>
        public static string[] Collected()
        {
            return (string[])CollectedGroups.Clone();
        }
    }
}
