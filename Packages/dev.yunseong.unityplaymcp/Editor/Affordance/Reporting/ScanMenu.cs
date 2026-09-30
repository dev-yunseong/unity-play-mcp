using System;
using System.IO;
using UnityPlayMcp.Affordances.Scan;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Editor
{
    /// <summary>
    /// 스캔을 수동으로 실행하는 메뉴.
    /// </summary>
    /// <remarks>
    /// 씬은 로드될 때마다 자동으로 스캔된다. 이 메뉴는 현재 화면을 다시 읽거나 모든 build 씬을 순회할 때 쓴다.
    /// 플레이 모드 밖에서는 저장된 값을 읽으므로 라벨이 placeholder 로 보인다.
    /// </remarks>
    internal static class ScanMenu
    {
        [MenuItem("Unity Play MCP/Scan Loaded Scenes", false, 0)]
        private static void Capture()
        {
            var path = AffordanceBootstrap.CaptureNow();

            if (path == null)
            {
                Debug.LogWarning("[Unity Play MCP] The report could not be written. See the warning above.");
                return;
            }

            Warn();
            Debug.Log("[Unity Play MCP] " + AffordanceReport.SceneCount + " scenes in the report: " + path);
        }

        [MenuItem("Unity Play MCP/Walk All Build Scenes", false, 1)]
        private static void Walk()
        {
            if (!AffordanceBootstrap.WalkAllScenes())
            {
                return;
            }

            Debug.Log("[Unity Play MCP] Walking every scene in Build Settings. " +
                      "The game in progress is discarded and the starting scene is restored at the end.");
        }

        /// <summary>
        /// Build Settings 등록 여부와 관계없이 프로젝트의 모든 씬 파일을 읽는다.
        /// </summary>
        /// <remarks>
        /// 씬을 address 로 로드하는 프로젝트는 Build Settings 에 씬을 등록하지 않는다.
        /// 플레이 모드 밖이므로 라벨은 <c>Start</c> 에서 넣는 값이 아니라 저장된 placeholder 이며, 이를 경고로 알린다.
        /// </remarks>
        [MenuItem("Unity Play MCP/Read Every Scene In The Project", false, 2)]
        private static void ReadAll()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Unity Play MCP] Not during play — this opens scenes in the editor, " +
                                 "which would end the run. Use Unity Play MCP / Walk All Build Scenes instead.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var opened = EditorSceneManager.GetActiveScene().path;
            var read = 0;

            AffordanceBootstrap.Forget();

            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // package 안의 씬은 게임에서 도달할 수 없으므로 `Assets/` 아래만 읽는다.
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    SceneEvidenceScan.CaptureLoaded();
                    read++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Unity Play MCP] " + path + " would not open: " + exception.Message);
                }
            }

            if (!string.IsNullOrEmpty(opened))
            {
                EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
            }

            Warn();
            Debug.Log("[Unity Play MCP] " + read + " scenes read: " + AffordanceBootstrap.Save());
        }

        /// <summary>
        /// 에디터에서 live 채널을 켜고 끈다.
        /// </summary>
        /// <remarks>
        /// 값을 주기적으로 읽는 비용이 있으므로 자동으로 켜지 않는다. 플레이 모드 밖에서 켜면 저장된 상태를
        /// 실행 중인 값처럼 보고하므로 플레이 모드에서만 허용한다.
        /// </remarks>
        [MenuItem("Unity Play MCP/Watch Live State", false, 10)]
        private static void Watch()
        {
            if (AffordanceBootstrap.Watching)
            {
                AffordanceBootstrap.StopWatching();
                Debug.Log("[Unity Play MCP] Stopped watching.");
                return;
            }

            if (!AffordanceBootstrap.WatchLiveState())
            {
                return;
            }

            Debug.Log("[Unity Play MCP] Watching. Readings go to " + UnityPlayMcp.Affordances.Live.PulseFile.Path);
        }

        [MenuItem("Unity Play MCP/Watch Live State", true)]
        private static bool CanWatch()
        {
            Menu.SetChecked("Unity Play MCP/Watch Live State", AffordanceBootstrap.Watching);
            return Application.isPlaying;
        }

        [MenuItem("Unity Play MCP/Reveal Readings", false, 22)]
        private static void RevealReadings()
        {
            var path = UnityPlayMcp.Affordances.Live.PulseFile.Path;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Unity Play MCP] No readings yet. Enter play mode and run Unity Play MCP / Watch Live State.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Unity Play MCP/Forget Everything Scanned", false, 20)]
        private static void Forget()
        {
            AffordanceBootstrap.Forget();
            Debug.Log("[Unity Play MCP] The report is empty again. It fills back up as scenes load.");
        }

        [MenuItem("Unity Play MCP/Reveal Report", false, 21)]
        private static void Reveal()
        {
            var path = AffordanceBootstrap.ReportPath;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Unity Play MCP] Nothing written yet. Enter play mode, or run Unity Play MCP / Scan Loaded Scenes.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        private static void Warn()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[Unity Play MCP] Scanned outside play mode. Fields read as their saved values, " +
                    "not as what the game sets during Awake and Start.");
            }
        }
    }
}
