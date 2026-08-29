using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 주소에 사는 씬들에 대해 순회에 일러 준다.
    /// </summary>
    /// <remarks>
    /// 이 어셈블리는 프로젝트에 Addressables 가 있을 때만 컴파일된다. asmdef 이 패키지 자신의 존재로부터
    /// <c>ARTEL_ADDRESSABLES</c> 를 켜고 그것을 요구하므로, 그 패키지가 없는 프로젝트는 전에 빌드하던 것을 그대로 빌드하고
    /// 이 파일은 컴파일러가 보기에 존재하지 않는다. 어딘가의 <c>#if</c> 가 아니라 제 어셈블리인 이유 전체가 그것이다.
    ///
    /// 디스크가 아니라 카탈로그에 묻는다. 빌드된 플레이어에는 애셋 데이터베이스도 씬 폴더도 없다. 그것이 가진 것은 게임 자신이
    /// 씬을 찾는 데 쓰는 locator 이고, 그것은 구성상 같은 목록이다.
    /// </remarks>
    internal static class AddressedScenes
    {
        /// <summary>순회가 포기하기까지 주소로 된 씬 하나가 걸릴 수 있는 시간.</summary>
        private const float Patience = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ExtraScenes.List = List;
            ExtraScenes.Load = Load;
#endif
        }

        /// <summary>
        /// 씬으로 해석되는 모든 주소.
        /// </summary>
        /// <remarks>
        /// locator 는 같은 애셋에 대해 여러 키에 답한다 — 그 주소, 그 guid, 그 라벨 하나하나. guid 는 아무도 쓴 것이 아니므로
        /// 버리고, 라벨은 resolver 가 고른 멤버가 무엇이든 그것을 띄우게 된다. 남는 것은 주소이고, 그것이 게임 자신이 쓰는
        /// 이름이다.
        /// </remarks>
        private static List<string> List()
        {
            var found = new List<string>();
            var seen = new HashSet<string>();

            foreach (var locator in Addressables.ResourceLocators)
            {
                if (locator?.Keys == null)
                {
                    continue;
                }

                foreach (var key in locator.Keys)
                {
                    if (!(key is string address) || IsGuid(address) || !seen.Add(address))
                    {
                        continue;
                    }

                    if (locator.Locate(key, typeof(SceneInstance), out var locations) &&
                        locations != null && locations.Count == 1)
                    {
                        // 정확히 하나여야 한다. 여럿으로 답하는 키는 주소가 아니라 라벨이고, 그것을 로드하면 먼저 온 무엇이 올라오기 때문이다.
                        found.Add(address);
                    }
                }
            }

            found.Sort(System.StringComparer.Ordinal);
            return found;
        }

        private static IEnumerator Load(string address)
        {
            var loading = Addressables.LoadSceneAsync(address, LoadSceneMode.Single);
            var waited = 0f;

            while (!loading.IsDone)
            {
                waited += Time.unscaledDeltaTime;

                if (waited > Patience)
                {
                    Debug.LogWarning("[Artel] " + address + " did not finish loading in " +
                                     Patience + "s. Moving on.");
                    yield break;
                }

                yield return null;
            }
        }

        private static bool IsGuid(string key)
        {
            if (key.Length != 32)
            {
                return false;
            }

            foreach (var character in key)
            {
                var hex = (character >= '0' && character <= '9') ||
                          (character >= 'a' && character <= 'f') ||
                          (character >= 'A' && character <= 'F');

                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
