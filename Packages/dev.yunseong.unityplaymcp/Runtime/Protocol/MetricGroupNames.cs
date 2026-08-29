namespace Artel.Protocol
{
    /// <summary>
    /// 성능 보고가 싣는 지표군의 와이어 이름.
    /// </summary>
    /// <remarks>
    /// 서버가 정한 계약이다. SDK가 이름을 발명하지 않는다 — 서버는 성능 보고의 최상위 객체
    /// 필드 중 자기가 이름 붙이지 않은 것을 전부 지표군으로 받으므로, 철자가 하나 어긋나면
    /// 오류 없이 새 군이 하나 생긴다. 조회 화면에는 값이 영영 안 붙은 군으로 남는다.
    ///
    /// 이름을 여기 모아 두는 이유는 <see cref="Dto.PerformanceMessageDto"/>의 직렬화 이름과
    /// <see cref="Collected"/> 목록이 같은 상수를 보게 하기 위해서다. 둘이 갈리면 서버는
    /// "선언했는데 값이 없다"고 읽어 이 플랫폼이 그 군을 지원하지 않는다고 답한다.
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
        /// 플랫폼에 따라 달라지지 않는다. 실제로 값이 나왔는지는 보고에 그 군이 실렸는지가
        /// 답하고, 이 목록은 "이 SDK 버전이 그 군을 아는가"만 답한다. 두 축을 겹치면 소비자가
        /// 구버전 SDK와 에디터 전용 군을 Standalone에서 구분하지 못한다.
        ///
        /// 아직 수집하지 않는 군을 미리 적지 않는다. 선언은 시도한다는 뜻이라, 시도하지 않는
        /// 군을 적으면 서버가 "재려 했으나 못 쟀다"고 답해 거짓말이 된다.
        /// </summary>
        private static readonly string[] CollectedGroups = { FrameTiming, EditorRender };

        /// <summary>
        /// <see cref="CollectedGroups"/>의 복사본.
        ///
        /// 배열 자체를 넘기면 보고 DTO에 실린 뒤 호출자가 내용을 바꿀 수 있고, 그러면 이후
        /// 세션이 조용히 다른 목록을 보낸다. 세션당 한 번 부르는 자리라 복사가 싸다.
        /// </summary>
        public static string[] Collected()
        {
            return (string[])CollectedGroups.Clone();
        }
    }
}
