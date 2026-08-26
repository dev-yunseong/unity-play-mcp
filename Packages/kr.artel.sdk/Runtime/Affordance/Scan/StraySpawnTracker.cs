using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Records every root object alive before a scene is visited, so the ones that visit leaves
    /// behind can be handed to the scene that is about to be unloaded and die with it.
    /// </summary>
    /// <remarks>
    /// A visited scene escapes its own unload two ways: <c>DontDestroyOnLoad</c> moves objects to
    /// a scene of their own, and anything its <c>Awake</c> or <c>Start</c> instantiates lands in
    /// whatever scene is active at the time — the game's, since a scene cannot be made active
    /// until it has finished loading. Both end up as new roots somewhere, which is what this
    /// tracks.
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
        /// 개수가 아니라 이름을 돌려주는 이유: 0은 남긴 것이 없었다는 뜻일 수도, 이 정리가 아예
        /// 돌지 않았다는 뜻일 수도 있는데 개수만으로는 그 둘이 구분되지 않는다. 어느 오브젝트가
        /// 죽었는지는 게임 저자가 미아와 제 것을 가릴 유일한 근거이기도 하다.
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

                    // 계기는 아니다. 숨은 루트는 씬이 남긴 무엇이 아니라 도구가 거기 둔 것이고, 이 순회는 그 둘을 언제 나타났는지로 가릴 수
                    // 없다: 순회는 시작하기 전에 존재하는 것을 기록하는데, SDK 자신의 carrier 는 연결이 열릴 때 만들어지고 — 그것은 순회
                    // 도중이다. 연결을 여는 일이 곧 순회를 시작시키기 때문이다.
                    //
                    // 판독 채널이 연결에서 돌기 시작한 뒤에 실측했다: pulse 의 carrier 가 실행 1초 뒤에 만들어져 처음 방문한 씬에서
                    // 미아로 세어졌고 그 씬과 함께 파괴됐다. 채널은 시작했다고 보고하고는 그 세션 내내 아무것도 쓰지 않았다.
                    //
                    // 판독 순회가 계기를 제 보고에서 빼는 데 쓰는 것과 같은 규칙이다. 제 숨은 루트를 만들어내는 게임은 그것을 그대로 두는데,
                    // 그쪽이 안전한 방향이다: 살려 둔 객체는 이것이 잡지 못한 미아 하나이지만, 잘못 가져간 객체는 실행 밑에서 뽑혀 나간
                    // 도구다.
                    if (root.hideFlags != HideFlags.None)
                    {
                        continue;
                    }

                    // Only roots can be moved between scenes. An object the visited scene parented
                    // under something the game owns is unreachable from here, and stays.
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
        /// The scene Unity parks <c>DontDestroyOnLoad</c> objects in, so its roots can be walked
        /// like any other scene's.
        /// </summary>
        public static Scene DontDestroyOnLoadScene()
        {
            // Unity hands out no reference to the DontDestroyOnLoad scene, and SceneManager does
            // not count it. Moving a throwaway object into it is the only way to read the handle
            // back, and it doubles as a way to make the scene exist at all — it does not until
            // something is put there.
            var probe = new GameObject("Artel DontDestroyOnLoad Probe");
            Object.DontDestroyOnLoad(probe);
            var resolved = probe.scene;
            Object.DestroyImmediate(probe);
            return resolved;
        }
    }
}
