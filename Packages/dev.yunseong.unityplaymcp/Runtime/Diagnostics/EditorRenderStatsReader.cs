// 렌더 통계는 UnityEditor.UnityStats 로만 읽을 수 있다. 런타임 어셈블리는 Editor 전용이 아니므로
// UnityEditor 참조가 #if 밖에 남으면 Standalone 빌드가 깨진다. UnityEditor 참조는 이 파일 안에만 둔다.
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// Game view Stats 창과 같은 렌더 통계를 읽는다.
    ///
    /// <c>UnityStats</c> 는 주입할 수 없는 정적 API 라 테스트할 수 없으므로 읽기만 한다.
    /// 단위 변환은 <c>EditorRenderStatsMapper</c> 에 둔다.
    /// </summary>
    internal static class EditorRenderStatsReader
    {
        /// <summary>
        /// 호출한 프레임의 순간값을 읽는다. 누적 상태가 없으므로 보낼 때만 불러도 된다.
        /// </summary>
        /// <returns>
        /// 에디터 밖에서는 항상 false 다. 이때 호출자는 렌더 항목을 뺀다. 0 을 채우면
        /// 아무것도 안 그린 프레임과 구분되지 않는다.
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
