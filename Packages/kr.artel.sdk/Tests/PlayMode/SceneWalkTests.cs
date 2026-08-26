using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Artel.Affordances.Scan;
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
    /// 추적기 하나만 떼어 보는 것과 달리, 여기서는 <see cref="AffordanceBootstrap.WalkAllScenes"/>가
    /// 쓰는 실제 워크를 돈다 — 실제 게임에서 persistent 오브젝트가 살아남은 경로다.
    /// </summary>
    /// <remarks>
    /// 워크는 Build Settings를 읽으므로 테스트가 씬 두 개를 만들어 등록한다. 씬 파일을 만드는
    /// 일은 플레이 모드에 들어가기 전에 끝나야 해서 <see cref="IPrebuildSetup"/>에 둔다. 원래
    /// 목록은 <see cref="IPostBuildCleanup"/>에서 되돌린다.
    ///
    /// 목록은 테스트 안에서 한 번 더 좁힌다. Test Framework가 플레이 모드에 들어가면서 제 임시
    /// 실행 씬을 목록에 끼워 넣는데, 워크가 그 씬까지 방문하면 실행 전체가 다시 돈다.
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
                new EditorBuildSettingsScene(PersistentScenePath, true),
                new EditorBuildSettingsScene(EmptyScenePath, true)
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
        public IEnumerator WalkAllScenes_DoomsPersistentRootLeftByAVisitedScene()
        {
#if UNITY_EDITOR
            // Test Framework는 플레이 모드로 들어가면서 제 임시 실행 씬을 Build Settings 끝에
            // 끼워 넣는다. 워크는 그 목록을 그대로 도므로 그 씬까지 Single로 다시 로드하고,
            // 그러면 러너가 하나 더 살아나 실행 전체가 처음부터 다시 돈다 — 뒤에 오는 픽스처가
            // 두 번째 판의 더럽혀진 상태에서 돌아 깨진다. 워크가 볼 목록을 여기서 우리 씬 둘로
            // 좁혀 그 임시 씬을 방문 대상에서 뺀다. IPrebuildSetup이 저장해 둔 원래 목록은
            // 그대로이므로 Cleanup이 되돌린다.
            //
            // persistent 씬을 먼저 둔다. 방문 씬이 남긴 root는 다음 Single 로드가 데려가는데,
            // 임시 씬을 뺀 뒤에는 워크가 돌아갈 origin이 없어 마지막 로드가 한 번 더 오지 않는다.
            // 그 자리에 놓인 씬의 잔여물은 죽을 기회를 못 얻는다.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(PersistentScenePath, true),
                new EditorBuildSettingsScene(EmptyScenePath, true)
            };
#endif

            var buildScenes = Enumerable
                .Range(0, SceneManager.sceneCountInBuildSettings)
                .Select(SceneUtility.GetScenePathByBuildIndex)
                .ToList();
            CollectionAssert.AreEqual(
                new[] { PersistentScenePath, EmptyScenePath },
                buildScenes,
                "워크가 볼 씬 목록이 이 테스트의 씬 둘로 좁혀지지 않았다.");
            Assert.IsNull(FindPersistentRoot(), "워크 전에 이미 픽스처가 살아 있다.");

            // 무엇을 걷어냈는지와 워크가 끝났다는 사실 자체가 로그로 남아야 한다. 이게 없으면
            // "0개 걷어냈다"와 "정리가 아예 안 돌았다"를 사후에 구분할 방법이 없다.
            LogAssert.Expect(
                LogType.Log,
                new Regex(@"^\[Artel\] Scene walk will unload 1 object\(s\) left behind by .*: " +
                          Regex.Escape(PersistentFixtureBehaviour.RootName) + @"\.$"));
            LogAssert.Expect(
                LogType.Log,
                new Regex(@"^\[Artel\] Walk finished\. \d+ scenes in the report; removed " +
                          @"1 object\(s\) left behind: .+$"));

            Assert.IsTrue(AffordanceBootstrap.WalkAllScenes(), "워크가 시작되지 않았다.");
            while (AffordanceBootstrap.Walking)
            {
                yield return null;
            }

            var leftover = FindPersistentRoot();
            Assert.IsNull(
                leftover,
                "방문 씬이 남긴 persistent root가 워크 뒤에도 살아 있다: " +
                (leftover == null ? string.Empty : leftover.scene.name));

            // 워크가 돌아갈 origin을 일부러 없앴으므로 활성 씬은 마지막 방문 씬 그대로다.
            // 원래 씬으로의 복귀는 이 테스트가 재는 것이 아니다.
            Assert.AreEqual(
                EmptyScenePath,
                SceneManager.GetActiveScene().path,
                "워크가 마지막 방문 씬에 서 있지 않다.");
        }

        private static GameObject FindPersistentRoot()
        {
            return Object.FindObjectsOfType<PersistentFixtureBehaviour>(true)
                .Select(fixture => fixture.gameObject)
                .FirstOrDefault();
        }
    }
}
