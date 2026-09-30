using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// Addressables 주소로 로드하는 씬을 순회에 알려 준다.
    /// </summary>
    /// <remarks>
    /// asmdef 이 Addressables 패키지가 있을 때만 <c>UNITY_PLAY_MCP_ADDRESSABLES</c> 를 켜고 요구하므로,
    /// 패키지가 없는 프로젝트에서는 이 어셈블리가 컴파일되지 않는다.
    /// 빌드된 플레이어에는 애셋 데이터베이스가 없으므로 디스크 대신 locator 에 묻는다.
    /// </remarks>
    internal static class AddressedScenes
    {
        /// <summary>씬 하나의 로드를 기다리는 최대 시간(초).</summary>
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
        /// locator 는 주소, guid, 라벨 모두에 답한다. guid 는 게임이 쓰는 이름이 아니고,
        /// 라벨은 여러 씬 중 아무것이나 로드하므로 주소만 남긴다.
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
                        // 여럿으로 답하는 키는 라벨이며, 로드하면 먼저 온 씬이 올라온다.
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
                    Debug.LogWarning("[Unity Play MCP] " + address + " did not finish loading in " +
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
