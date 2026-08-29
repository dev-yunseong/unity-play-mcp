// 렌더 통계는 UnityEditor.UnityStats로만 읽을 수 있다. 런타임 어셈블리에는 Editor 전용 플랫폼
// 제약이 없어서, UnityEditor 참조가 한 줄이라도 남으면 Standalone 빌드의 컴파일이 깨진다.
// 조건부 컴파일이 유일한 방어선이자 가장 강한 보장이다 — 플레이어 컴파일 단위에 이 심볼 자체가
// 존재하지 않는다. 이 파일 밖으로 UnityEditor 참조를 내보내지 말 것.
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Artel.Diagnostics
{
    /// <summary>
    /// Game view Stats 창과 같은 렌더 통계를 읽는다.
    ///
    /// <c>UnityStats</c>는 주입할 수 없는 정적 API라 이 경계 안쪽은 테스트가 닿지 않는다.
    /// 그래서 읽기만 하고 계산은 하지 않는다 — 단위 변환도 <c>EditorRenderStatsMapper</c>에 둔다.
    /// <c>RuntimeEnvironment</c>와 같은 이유, 같은 모양이다.
    /// </summary>
    internal static class EditorRenderStatsReader
    {
        /// <summary>
        /// 부르는 그 프레임의 순간값을 읽는다. 누적 상태가 없어 건너뛴 호출이 다음 값을 왜곡하지
        /// 않으므로, 전송 게이트가 열릴 때만 불러도 된다.
        /// </summary>
        /// <returns>
        /// 에디터 밖에서는 항상 false. 호출자는 false면 보고에서 렌더 항목을 통째로 뺀다.
        /// 0을 채워 보내면 "에디터가 아니라 못 잰 것"과 "정말 아무것도 안 그린 프레임"이
        /// 구분되지 않는다.
        /// </returns>
        public static bool TryRead(out EditorRenderStats stats)
        {
#if UNITY_EDITOR
            stats = new EditorRenderStats(
                UnityStats.drawCalls,
                UnityStats.batches,
                UnityStats.setPassCalls,
                UnityStats.triangles,
                UnityStats.vertices,
                UnityStats.frameTime,
                UnityStats.renderTime);
            return true;
#else
            stats = default;
            return false;
#endif
        }
    }
}
