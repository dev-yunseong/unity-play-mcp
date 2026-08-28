using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// Walks every scene listed in Build Settings and scans each one.
    /// </summary>
    internal sealed class AllSceneScanner
    {
        /// <summary>
        /// How long a freshly loaded scene is left alone before it is read.
        /// Awake and Start are covered well inside this; work a scene defers further than this —
        /// a coroutine, an Invoke, a web request callback — is not, and no fixed value would
        /// cover it.
        /// </summary>
        internal const float SettleSeconds = 0.1f;

        private readonly SceneScanner scanner;

        public AllSceneScanner(SceneScanner scanner)
        {
            this.scanner = scanner;
        }

        /// <param name="progress">
        /// Called as each scene is about to be visited, with the 1-based position of that scene
        /// and the total. The walk loads and tears down one scene at a time, so a caller hiding
        /// the screen for its duration has nothing else to show the player.
        /// </param>
        public IEnumerator ScanAll(
            SceneScanOptions options,
            Action<List<ScannedSceneDto>> completed,
            Action<int, int> progress = null)
        {
            var scanned = new List<ScannedSceneDto>();
            var originalScene = SceneManager.GetActiveScene();

            var strays = new StraySpawnTracker();
            var removed = 0;

            var sceneCount = SceneManager.sceneCountInBuildSettings;
            for (var buildIndex = 0; buildIndex < sceneCount; buildIndex++)
            {
                progress?.Invoke(buildIndex + 1, sceneCount);
                var path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                var scene = SceneManager.GetSceneByPath(path);

                // Scenes are loaded additively rather than singly: a single load destroys the
                // running scene, and this SDK lives in it — the coroutine driving this walk would
                // die halfway through. It also means a scene the game already has open is scanned
                // in place instead of being duplicated and torn back down.
                var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
                if (!wasAlreadyLoaded)
                {
                    strays.Capture();
                    yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                    scene = SceneManager.GetSceneByPath(path);

                    // Awake and OnEnable ran during the load, Start runs on the next frame, and
                    // scenes routinely fill their UI a little after that. Unscaled, because a
                    // game sitting at timeScale 0 would otherwise wait here forever.
                    yield return new WaitForSecondsRealtime(SettleSeconds);
                }

                // 워크는 씬 핸들을 두 번의 대기 너머로 들고 있고, 그 사이에 게임이 제 씬을
                // Single로 로드하는 것을 막을 방법이 없다. 아무 키에나 씬을 바꾸는 Update 하나면
                // 충분하다 — 등록 덮개는 포인터를 막지 키보드를 막지 않는다. Single 로드는 워크가
                // 들고 있던 씬을 전부 파괴하고, 그 뒤 아래의 모든 호출은 사라진 것을 가리키는
                // 핸들에 대고 도는 셈이 된다. 여기서 멈추면 원인이 이름을 갖고, 그냥 진행하면
                // 가장 먼저 터지는 곳의 예외에 원인이 묻힌다.
                if (!scene.IsValid() || !scene.isLoaded ||
                    !originalScene.IsValid() || !originalScene.isLoaded)
                {
                    Debug.LogError(
                        "[Artel] Scene walk stopped at " + path +
                        ": the game loaded a scene of its own while the walk was running, so the " +
                        "scenes it was holding are gone. Anything they left behind stays behind.");
                    break;
                }

                Scan(scene, buildIndex, path, options, scanned);

                if (!wasAlreadyLoaded)
                {
                    // Collected and unloaded without yielding in between, so nothing spawned
                    // after the comparison slips past it.
                    removed += Collect(strays, scene, path);
                    RestoreActiveScene(originalScene);

                    if (scene.isLoaded)
                    {
                        yield return SceneManager.UnloadSceneAsync(scene);
                    }
                }
            }

            RestoreActiveScene(originalScene);

            // 결과가 무엇이든 남긴다. 0도 남긴다. 아무것도 안 남긴 워크와 정리가 아예 돌지
            // 않은 워크는 로그가 없으면 똑같이 보이고, 사후에 그 둘을 가릴 방법이 없다.
            Debug.Log(
                "[Artel] Scene walk visited " + scanned.Count + " of " + sceneCount +
                " scene(s) and removed " + removed + " object(s) left behind.");

            // Target ids come from whichever scene was scanned last, and that scene is now
            // unloaded. Rescan so button_click and enter_text address live objects again — with
            // the default options, so a full walk does not leave inactive objects sitting in the
            // target map for the actions that follow it in the batch.
            scanner.Scan();
            completed(scanned);
        }

        /// <summary>
        /// 방문한 씬 하나를 읽어 <paramref name="scanned"/>에 담는다. 터진 씬은 보고에서 빠지고
        /// 워크는 계속 간다: 뒤따르는 씬들은 이 씬이 읽혔는지와 무관하게 읽을 수 있고, 씬 하나
        /// 때문에 나머지를 전부 잃는 쪽이 손해가 크다.
        /// </summary>
        private void Scan(
            Scene scene,
            int buildIndex,
            string path,
            SceneScanOptions options,
            List<ScannedSceneDto> scanned)
        {
            try
            {
                SceneManager.SetActiveScene(scene);
                scanned.Add(new ScannedSceneDto
                {
                    BuildIndex = buildIndex,
                    Path = path,

                    // The scan result's pending actions are deliberately left uncommitted. These
                    // scenes are being visited, not played, and dropping their recorded actions
                    // here would hide them from the next GAME_STATE.
                    Scene = SceneSnapshotMapper.ToDto(scanner.Scan(options).Scene)
                });
            }
            catch (Exception exception)
            {
                Debug.LogError("[Artel] Scene walk could not read " + path + ".");
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 방문 씬이 남긴 것을 그 씬에게 넘기고, 무엇을 가져갔는지 말한다. 여기서 실패해도 뒤따르는
        /// 언로드를 건너뛰면 안 된다. 건너뛰면 그 씬이 게임 위에 얹힌 채로 남는다.
        /// </summary>
        private static int Collect(StraySpawnTracker strays, Scene scene, string path)
        {
            try
            {
                var moved = strays.MoveInto(scene);
                if (moved.Count > 0)
                {
                    Debug.Log(
                        "[Artel] Unloading " + moved.Count + " object(s) left behind by " + path +
                        ": " + string.Join(", ", moved) + ".");
                }

                return moved.Count;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Artel] Scene walk could not collect what " + path + " left behind.");
                Debug.LogException(exception);
                return 0;
            }
        }

        /// <summary>
        /// 게임이 제 씬을 로드했다면 워크가 출발한 씬은 이미 없다. 사라진 씬을 활성화해 달라고
        /// Unity에 조르면 진짜 실패 위에 잡음만 얹힌다.
        /// </summary>
        private static void RestoreActiveScene(Scene originalScene)
        {
            if (originalScene.IsValid() && originalScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalScene);
            }
        }
    }
}
