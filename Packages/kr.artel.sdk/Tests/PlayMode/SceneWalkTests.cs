using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Artel.Protocol.Dto;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Artel.Tests
{
    /// <summary>
    /// 씬 워크 전체를 실제 Build Settings 씬으로 돌린다. <see cref="StraySpawnTrackerTests"/>가
    /// 추적기 하나만 떼어 보는 것과 달리, 여기서는 <see cref="AllSceneScanner"/>가 씬을 얹고
    /// 스캔하고 내리는 순서 그대로 돈다 — 실제 게임에서 persistent 오브젝트가 살아남은 경로다.
    /// </summary>
    /// <remarks>
    /// 워크는 Build Settings를 읽으므로 테스트가 씬 두 개를 만들어 등록한다. 그 작업은 플레이
    /// 모드에 들어가기 전에 끝나야 해서 <see cref="IPrebuildSetup"/>에 둔다. 원래 목록은
    /// <see cref="IPostBuildCleanup"/>에서 되돌린다.
    /// </remarks>
    public sealed class SceneWalkTests : IPrebuildSetup, IPostBuildCleanup
    {
        private const string TestFolder = "Assets/ArtelSceneWalkTests";
        private const string EmptyScenePath = TestFolder + "/Artel Walk Empty.unity";
        private const string PersistentScenePath = TestFolder + "/Artel Walk Persistent.unity";

        // Setup과 Cleanup 사이에 플레이 모드 진입이 끼어 도메인 리로드가 정적 필드를 날린다.
        // 원래 Build Settings 목록은 EditorPrefs로 넘긴다.
        private const string SavedScenesKey = "Artel.Tests.SceneWalkTests.SavedBuildScenes";

        public void Setup()
        {
#if UNITY_EDITOR
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArtelSceneWalkTests");
            }

            CreateScene(EmptyScenePath, withPersistentRoot: false);
            CreateScene(PersistentScenePath, withPersistentRoot: true);
            AssetDatabase.Refresh();

            EditorPrefs.SetString(
                SavedScenesKey,
                string.Join("\n", EditorBuildSettings.scenes.Select(entry => entry.path)));

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(EmptyScenePath, true),
                new EditorBuildSettingsScene(PersistentScenePath, true)
            };
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            if (EditorPrefs.HasKey(SavedScenesKey))
            {
                var saved = EditorPrefs.GetString(SavedScenesKey);
                EditorBuildSettings.scenes = string.IsNullOrEmpty(saved)
                    ? new EditorBuildSettingsScene[0]
                    : saved.Split('\n')
                        .Select(path => new EditorBuildSettingsScene(path, true))
                        .ToArray();
                EditorPrefs.DeleteKey(SavedScenesKey);
            }

            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.Refresh();
#endif
        }

#if UNITY_EDITOR
        private static void CreateScene(string path, bool withPersistentRoot)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (withPersistentRoot)
            {
                // 에디트 모드라 Awake는 돌지 않는다. DontDestroyOnLoad는 이 씬이 플레이 모드에서
                // 얹힐 때 처음 불린다 — 워크가 마주치는 상황 그대로다.
                var root = new GameObject(
                    PersistentFixtureBehaviour.RootName,
                    typeof(PersistentFixtureBehaviour));
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
        }
#endif

        /// <summary>
        /// 방문 씬이 <c>DontDestroyOnLoad</c>로 빼돌린 오브젝트를 워크가 걷어내는지 본다.
        /// 걷어내지 못하면 그 오브젝트는 스캔이 끝난 뒤에도 게임 위에 계속 남는다.
        /// </summary>
        [UnityTest]
        public IEnumerator ScanAll_DoomsPersistentRootLeftByAVisitedScene()
        {
            // Test Framework가 플레이 모드 실행용 임시 씬을 Build Settings에 끼워 넣으므로
            // 개수로 단언하지 않는다. 우리 씬 둘이 들어 있는지만 본다.
            var buildScenes = Enumerable
                .Range(0, SceneManager.sceneCountInBuildSettings)
                .Select(SceneUtility.GetScenePathByBuildIndex)
                .ToList();
            CollectionAssert.Contains(buildScenes, PersistentScenePath, "IPrebuildSetup이 돌지 않았다.");
            CollectionAssert.Contains(buildScenes, EmptyScenePath, "IPrebuildSetup이 돌지 않았다.");
            Assert.IsNull(FindPersistentRoot(), "워크 전에 이미 픽스처가 살아 있다.");

            var originalScene = SceneManager.GetActiveScene();
            var sceneCountBefore = SceneManager.sceneCount;

            // 무엇을 걷어냈는지와 워크가 끝났다는 사실 자체가 로그로 남아야 한다. 이게 없으면
            // "0개 걷어냈다"와 "정리가 아예 안 돌았다"를 사후에 구분할 방법이 없다.
            LogAssert.Expect(
                LogType.Log,
                new Regex(@"^\[Artel\] Unloading 1 object\(s\) left behind by .*: " +
                          Regex.Escape(PersistentFixtureBehaviour.RootName) + @"\.$"));
            LogAssert.Expect(
                LogType.Log,
                new Regex(@"^\[Artel\] Scene walk visited \d+ of \d+ scene\(s\) and " +
                          @"removed 1 object\(s\) left behind\.$"));

            List<ScannedSceneDto> scanned = null;
            yield return new AllSceneScanner(new SceneScanner())
                .ScanAll(SceneScanOptions.Default, result => scanned = result);

            Assert.IsNotNull(scanned, "워크가 결과를 넘기지 않았다.");
            CollectionAssert.Contains(
                scanned.Select(entry => entry.Path).ToList(),
                PersistentScenePath,
                "워크가 persistent root를 담은 씬을 방문하지 않았다.");

            var leftover = FindPersistentRoot();
            Assert.IsNull(
                leftover,
                "방문 씬이 남긴 persistent root가 워크 뒤에도 살아 있다: " +
                (leftover == null ? string.Empty : leftover.scene.name));

            Assert.AreEqual(sceneCountBefore, SceneManager.sceneCount, "방문 씬이 언로드되지 않았다.");
            Assert.AreEqual(originalScene, SceneManager.GetActiveScene(), "활성 씬이 복구되지 않았다.");
        }

        private static GameObject FindPersistentRoot()
        {
            return Object.FindObjectsOfType<PersistentFixtureBehaviour>(true)
                .Select(fixture => fixture.gameObject)
                .FirstOrDefault();
        }
    }
}
