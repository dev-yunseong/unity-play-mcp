using System;
using System.Collections;
using System.Collections.Generic;
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
        /// How long a freshly loaded scene is left alone before it is scanned and torn down.
        /// Awake and Start are covered well inside this; work a scene defers further than this —
        /// a coroutine, an Invoke, a web request callback — is not, and no fixed value would
        /// cover it.
        /// </summary>
        private const float SettleSeconds = 0.1f;

        private readonly SceneScanner scanner;

        public AllSceneScanner(SceneScanner scanner)
        {
            this.scanner = scanner;
        }

        public IEnumerator ScanAll(SceneScanOptions options, Action<List<ScannedSceneDto>> completed)
        {
            var scanned = new List<ScannedSceneDto>();
            var originalScene = SceneManager.GetActiveScene();

            var strays = new StraySpawnTracker();

            for (var buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
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

                if (!wasAlreadyLoaded)
                {
                    // Collected and unloaded without yielding in between, so nothing spawned
                    // after the comparison slips past it.
                    var strayCount = strays.MoveInto(scene);
                    SceneManager.SetActiveScene(originalScene);
                    yield return SceneManager.UnloadSceneAsync(scene);

                    if (strayCount > 0)
                    {
                        Debug.Log(
                            "[Artel] Unloaded " + strayCount +
                            " object(s) left behind by " + path + ".");
                    }
                }
            }

            SceneManager.SetActiveScene(originalScene);

            // Target ids come from whichever scene was scanned last, and that scene is now
            // unloaded. Rescan so button_click and enter_text address live objects again — with
            // the default options, so a full walk does not leave inactive objects sitting in the
            // target map for the actions that follow it in the batch.
            scanner.Scan();
            completed(scanned);
        }
    }
}
