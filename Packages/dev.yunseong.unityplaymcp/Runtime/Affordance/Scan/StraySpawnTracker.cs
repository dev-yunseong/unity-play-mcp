using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// Records root objects alive before a scene visit, so roots the visit leaves behind can be
    /// moved into the scene being unloaded and destroyed with it.
    /// </summary>
    /// <remarks>
    /// Leftovers come from <c>DontDestroyOnLoad</c> and from objects that <c>Awake</c> or
    /// <c>Start</c> instantiates into the still-active previous scene. Both appear as new roots.
    /// </remarks>
    public sealed class StraySpawnTracker
    {
        private readonly HashSet<int> preexisting = new HashSet<int>();

        public void Capture()
        {
            preexisting.Clear();
            foreach (var scene in LoadedScenes())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    preexisting.Add(root.GetInstanceID());
                }
            }
        }

        /// <summary>
        /// Moves every root that appeared since <see cref="Capture"/> into <paramref name="doomed"/>,
        /// skipping that scene's own contents. Returns the names of what it moved. Unloading
        /// <paramref name="doomed"/> then destroys them along with it, running their
        /// <c>OnDestroy</c> as a normal unload would.
        /// </summary>
        /// <remarks>
        /// 개수 0 은 남긴 것이 없는 경우와 정리가 돌지 않은 경우를 구분하지 못하므로 이름을 돌려준다.
        /// 게임 저자가 무엇이 파괴됐는지 확인하는 데도 쓴다.
        /// </remarks>
        public List<string> MoveInto(Scene doomed)
        {
            var moved = new List<string>();
            foreach (var scene in LoadedScenes())
            {
                if (scene == doomed)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (preexisting.Contains(root.GetInstanceID()))
                    {
                        continue;
                    }

                    // hideFlags 가 있는 루트는 SDK 의 `carrier` 같은 도구 객체다. `carrier` 는 순회 도중에 만들어질 수 있어
                    // 시작 시점 기록으로는 구분되지 않고, 옮기면 `pulse` 가 멈춘다. `pulse` 가 도구 객체를 빼는 규칙과 같다.
                    // 게임의 숨은 루트도 남지만, 남겨 두는 쪽이 안전하다.
                    if (root.hideFlags != HideFlags.None)
                    {
                        continue;
                    }

                    // Only roots can move between scenes; children parented under game objects stay.
                    SceneManager.MoveGameObjectToScene(root, doomed);
                    moved.Add(root.name);
                }
            }

            return moved;
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    yield return scene;
                }
            }

            yield return DontDestroyOnLoadScene();
        }

        /// <summary>
        /// The scene that holds <c>DontDestroyOnLoad</c> objects.
        /// </summary>
        public static Scene DontDestroyOnLoadScene()
        {
            // Unity exposes no handle to this scene. A probe object reveals it and also makes the
            // scene exist, since it is created only when something is put there.
            var probe = new GameObject("Unity Play MCP DontDestroyOnLoad Probe");
            Object.DontDestroyOnLoad(probe);
            var resolved = probe.scene;
            Object.DestroyImmediate(probe);
            return resolved;
        }
    }
}
