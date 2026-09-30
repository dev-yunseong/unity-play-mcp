using System;
using System.Collections;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 순회가 씬 하나를 읽은 직후 바깥 코드를 부르는 hook.
    /// </summary>
    /// <remarks>
    /// 이 어셈블리는 <c>UnityPlayMcp.Runtime</c> 을 참조하지 않으므로 그쪽이 hook 을 등록한다.
    /// 캡처가 프레임 렌더링을 기다려야 하므로 coroutine 을 돌려받는다.
    /// <b>구독자는 예외를 직접 삼켜야 한다.</b> 순회는 <c>yield return</c> 을 <c>try</c> 로 감쌀 수 없어,
    /// 새어 나온 예외가 순회를 멈추고 `evidence` 문서가 나오지 않는다.
    /// </remarks>
    public static class SceneWalkHooks
    {
        /// <summary>
        /// 씬 하나를 읽은 직후 순회가 부른다. 인자는 그 씬의 이름이고, null 을 돌려주면 순회는 곧바로 다음 씬으로 간다.
        /// </summary>
        public static Func<string, IEnumerator> SceneRead;

        /// <summary>hook 의 coroutine. 구독자가 없거나 할 일이 없으면 null.</summary>
        internal static IEnumerator OnSceneRead(string sceneName)
        {
            var hook = SceneRead;

            if (hook == null)
            {
                return null;
            }

            try
            {
                return hook(sceneName);
            }
            catch (Exception exception)
            {
                // coroutine 생성 중 예외만 여기서 잡힌다. 실행 중 예외는 구독자가 처리한다.
                UnityEngine.Debug.LogWarning(
                    "[Unity Play MCP] A scene-read hook threw before it started and was skipped: " + exception.Message);
                return null;
            }
        }
    }
}
