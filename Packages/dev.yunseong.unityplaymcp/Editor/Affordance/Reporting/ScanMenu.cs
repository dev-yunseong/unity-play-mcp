using System;
using System.IO;
using Artel.Affordances.Scan;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// 손으로 스캔을 청한다.
    /// </summary>
    /// <remarks>
    /// 씬은 로드될 때마다 읽히므로 게임을 하는 동안 리포트가 저절로 채워진다. 그래도 청해야 하는 것이 둘 남는다:
    /// 로더가 스스로 닿지 못하는 데로 게임을 몰고 간 뒤에 화면을 다시 읽는 일, 그리고 하나하나 플레이해 가지 않고
    /// 빌드의 모든 씬으로 가 보는 일.
    ///
    /// 이것들을 쓸 값이 있는 자리는 플레이 모드다. 그 밖에서는 계층이 도는 것이 아니라 저장된 것을 보여 주므로,
    /// 라벨이 갈아 끼워지지 않은 자리표시자로 읽힌다.
    /// </remarks>
    internal static class ScanMenu
    {
        [MenuItem("Artel/Scan Loaded Scenes", false, 0)]
        private static void Capture()
        {
            var path = AffordanceBootstrap.CaptureNow();

            if (path == null)
            {
                Debug.LogWarning("[Artel] The report could not be written. See the warning above.");
                return;
            }

            Warn();
            Debug.Log("[Artel] " + AffordanceReport.SceneCount + " scenes in the report: " + path);
        }

        [MenuItem("Artel/Walk All Build Scenes", false, 1)]
        private static void Walk()
        {
            if (!AffordanceBootstrap.WalkAllScenes())
            {
                return;
            }

            Debug.Log("[Artel] Walking every scene in Build Settings. " +
                      "The game in progress is discarded and the starting scene is restored at the end.");
        }

        /// <summary>
        /// 빌드가 알든 모르든, 프로젝트의 모든 씬 파일을 읽는다.
        /// </summary>
        /// <remarks>
        /// 씬을 주소로 로드하는 프로젝트는 그것을 Build Settings 에 등록할 이유가 없고, 최근에 시작된 프로젝트일수록
        /// 그럴 가능성이 크다. Chop Chop 은 등록된 씬이 하나이고 디스크에는 쉰이 있다. 거기서 빌드 설정을 순회하면 화면
        /// 하나를 서술하고 그것을 게임이라고 부른다.
        ///
        /// 플레이 모드 밖이므로 이것은 도는 것이 아니라 저장된 것을 읽는다 — 라벨은 <c>Start</c> 에서 게임이 넣는 텍스트가
        /// 아니라 작성된 자리표시자를 보여 준다. 그것이 하나하나 플레이하지 않고 모든 화면에 닿는 값이고, 발견되도록 두는
        /// 대신 경고에 적어 둔다.
        /// </remarks>
        [MenuItem("Artel/Read Every Scene In The Project", false, 2)]
        private static void ReadAll()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Artel] Not during play — this opens scenes in the editor, " +
                                 "which would end the run. Use Artel / Walk All Build Scenes instead.");
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

                // 프로젝트 자신이 쥔 것만 본다. 가져온 패키지 안의 씬은 그 패키지 작성자의 것이지, 이 게임의 플레이어가 닿을 수
                // 있는 무엇이 아니다.
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
                    Debug.LogWarning("[Artel] " + path + " would not open: " + exception.Message);
                }
            }

            if (!string.IsNullOrEmpty(opened))
            {
                EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
            }

            Warn();
            Debug.Log("[Artel] " + read + " scenes read: " + AffordanceBootstrap.Save());
        }

        /// <summary>
        /// 에디터에서 라이브 채널을 켜고 끈다.
        /// </summary>
        /// <remarks>
        /// 감시는 일부러 자동이 아니다 — 게임의 값을 초당 열 번 읽는 일은 그 채널을 청한 쪽이 치를 값이고, 대부분의
        /// 프로젝트는 리포트만 원한다. 그러면 에디터에서 그것을 켤 수 있는 것이 아무것도 없게 되므로, 채널을 손으로 써 보려는
        /// 사람은 메서드 하나를 부르는 스크립트를 써야 했다. 이것이 메뉴를 단 그 메서드다.
        ///
        /// 플레이 모드에서만이고, 조용히 무시하는 대신 그렇다고 말한다: 게임이 돌기 전까지 아무것도 값을 쥐고 있지 않고,
        /// 멈춘 에디터에 대고 켠 채널은 씬의 저장된 상태를 테스터가 보고 있는 것인 양 보고하게 된다.
        /// </remarks>
        [MenuItem("Artel/Watch Live State", false, 10)]
        private static void Watch()
        {
            if (AffordanceBootstrap.Watching)
            {
                AffordanceBootstrap.StopWatching();
                Debug.Log("[Artel] Stopped watching.");
                return;
            }

            if (!AffordanceBootstrap.WatchLiveState())
            {
                return;
            }

            Debug.Log("[Artel] Watching. Readings go to " + Artel.Affordances.Live.PulseFile.Path);
        }

        [MenuItem("Artel/Watch Live State", true)]
        private static bool CanWatch()
        {
            Menu.SetChecked("Artel/Watch Live State", AffordanceBootstrap.Watching);
            return Application.isPlaying;
        }

        [MenuItem("Artel/Reveal Readings", false, 22)]
        private static void RevealReadings()
        {
            var path = Artel.Affordances.Live.PulseFile.Path;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Artel] No readings yet. Enter play mode and run Artel / Watch Live State.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Artel/Forget Everything Scanned", false, 20)]
        private static void Forget()
        {
            AffordanceBootstrap.Forget();
            Debug.Log("[Artel] The report is empty again. It fills back up as scenes load.");
        }

        [MenuItem("Artel/Reveal Report", false, 21)]
        private static void Reveal()
        {
            var path = AffordanceBootstrap.ReportPath;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Artel] Nothing written yet. Enter play mode, or run Artel / Scan Loaded Scenes.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        private static void Warn()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[Artel] Scanned outside play mode. Fields read as their saved values, " +
                    "not as what the game sets during Awake and Start.");
            }
        }
    }
}
