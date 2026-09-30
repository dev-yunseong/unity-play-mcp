using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 리포트가 게임 전체를 다루도록 빌드의 모든 씬을 차례로 로드해 읽는다.
    /// </summary>
    /// <remarks>
    /// 진행 중이던 실행을 버리므로 명시적으로 요청할 때만 시작한다.
    /// 도중에 멈춘 순회가 게임 위에 씬을 남기지 않도록 Additive 가 아니라 Single 로 로드한다.
    /// </remarks>
    internal sealed class SceneWalk : MonoBehaviour
    {
        /// <summary>씬을 읽기 전에 기다리는 프레임 수.</summary>
        /// <remarks>
        /// 로드 완료에 하나, 첫 <c>Update</c> 에 하나. 이후에는 <c>Awake</c>/<c>Start</c> 가 채운 텍스트를 읽는다.
        /// </remarks>
        private const int SettleFrames = 2;

        /// <summary>씬 하나의 로드를 기다리는 최대 시간(초).</summary>
        private const float PatiencePerScene = 30f;

        private static SceneWalk _walking;

        private readonly StraySpawnTracker strays = new StraySpawnTracker();
        private int removed;

        internal static bool InProgress => _walking != null;

        internal static bool Begin()
        {
            if (_walking != null)
            {
                return false;
            }

            var carrier = new GameObject("Unity Play MCP Scene Walk") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(carrier);

            _walking = carrier.AddComponent<SceneWalk>();
            _walking.StartCoroutine(_walking.Visit());
            return true;
        }

        private IEnumerator Visit()
        {
            var count = SceneManager.sceneCountInBuildSettings;
            var addressed = Addressed();

            if (count == 0 && addressed.Count == 0)
            {
                // Build Settings 가 비었지만 주소를 나열할 수 없는 경우를 씬이 없는 경우와 구분한다.
                Debug.LogWarning(ExtraScenes.Available
                    ? "[Unity Play MCP] No scenes in Build Settings and none reachable by address."
                    : "[Unity Play MCP] No scenes in Build Settings. If this game loads its scenes by " +
                      "address, install Addressables support and walk again.");

                AffordanceReport.Merge("(walk)", string.Empty, new List<string>
                {
                    ExtraScenes.Available ? "no-scenes-anywhere" : "no-scenes-in-build-settings"
                });

                Finish();
                yield break;
            }

            // 순회 후 돌아갈 씬. 이름은 폴더가 달라도 겹칠 수 있으므로 빌드 인덱스를 쓴다.
            var origin = SceneManager.GetActiveScene().buildIndex;

            for (var index = 0; index < count; index++)
            {
                Debug.Log("[Unity Play MCP] Walking scene " + (index + 1) + " of " + count + ".");
                strays.Capture();
                yield return Read(index);
            }

            for (var index = 0; index < addressed.Count; index++)
            {
                Debug.Log("[Unity Play MCP] Walking addressed scene " + (index + 1) + " of " + addressed.Count + ".");
                strays.Capture();
                yield return Read(addressed[index]);
            }

            if (origin >= 0)
            {
                yield return Load(origin);
            }

            // DontDestroyOnLoad 객체는 순회 동안 쌓이므로 끝에서 한 번 읽는다. `carrier` 가 그 씬에 있어 핸들을 얻을 수 있다.
            SceneEvidenceScan.CapturePersistent(gameObject.scene);

            Debug.Log("[Unity Play MCP] Walk finished. " + AffordanceReport.SceneCount + " scenes in the report; removed " +
                      removed + " object(s) left behind: " + AffordanceBootstrap.Save());

            Finish();
        }

        /// <summary>빌드 설정에 없는 씬들의 주소. 없으면 빈 목록.</summary>
        private static List<string> Addressed()
        {
            if (!ExtraScenes.Available)
            {
                return new List<string>();
            }

            try
            {
                return ExtraScenes.List() ?? new List<string>();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Unity Play MCP] The address catalogue could not be read: " + exception.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// 주소로 된 씬 하나를 Single 로 로드해 읽는다.
        /// </summary>
        /// <remarks>
        /// 이런 씬은 대개 매니저 씬 위에 Additive 로 올라가도록 만들어져 혼자서는 불완전할 수 있다.
        /// 읽은 내용은 버리지 않고 <c>scene-loaded-alone</c> 으로 표시한다.
        /// </remarks>
        private IEnumerator Read(string address)
        {
            IEnumerator loading;

            try
            {
                loading = ExtraScenes.Load(address);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Unity Play MCP] " + address + " would not load: " + exception.Message);
                yield break;
            }

            if (loading != null)
            {
                yield return loading;
            }

            for (var frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }

            SceneEvidenceScan.CaptureLoaded();
            AffordanceReport.Note(SceneManager.GetActiveScene().name, "scene-loaded-alone");

            // 다음 씬 로드 전에, 그리고 Collect 가 남은 객체를 옮기기 전에 화면을 남긴다.
            var afterAddressed = SceneWalkHooks.OnSceneRead(SceneManager.GetActiveScene().name);

            if (afterAddressed != null)
            {
                yield return afterAddressed;
            }

            Collect(SceneManager.GetActiveScene(), address);
        }

        private IEnumerator Read(int buildIndex)
        {
            yield return Load(buildIndex);

            var scene = SceneManager.GetActiveScene();

            if (scene.buildIndex != buildIndex)
            {
                // 스스로 다른 씬으로 넘어갔거나 로드에 실패했다. 도착한 씬 이름으로 기록하지 않고 gap 으로 남긴다.
                AffordanceReport.Merge("build:" + buildIndex, string.Empty,
                    new System.Collections.Generic.List<string> { "scene-would-not-stay" });
                yield break;
            }

            SceneEvidenceScan.Capture(scene);

            var afterScene = SceneWalkHooks.OnSceneRead(scene.name);

            if (afterScene != null)
            {
                yield return afterScene;
            }

            Collect(scene, SceneUtility.GetScenePathByBuildIndex(buildIndex));
        }

        /// <summary>방문 씬이 남긴 새 root 를 방문 씬으로 옮긴다.</summary>
        /// <remarks>
        /// 다음 <c>Single</c> 로드가 방문 씬과 함께 파괴한다. <c>Destroy</c> 를 바로 부르면
        /// 다음 씬의 <c>Awake</c> 보다 늦게 파괴돼 singleton 중복 검사를 오염시킬 수 있다.
        /// </remarks>
        private void Collect(Scene scene, string identity)
        {
            var moved = strays.MoveInto(scene);
            removed += moved.Count;

            if (moved.Count > 0)
            {
                Debug.Log(
                    "[Unity Play MCP] Scene walk will unload " + moved.Count + " object(s) left behind by " +
                    identity + ": " + string.Join(", ", moved) + ".");
            }
        }

        private IEnumerator Load(int buildIndex)
        {
            AsyncOperation loading;

            try
            {
                loading = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Unity Play MCP] Scene " + buildIndex + " would not load: " + exception.Message);
                yield break;
            }

            if (loading == null)
            {
                yield break;
            }

            var waited = 0f;

            while (!loading.isDone)
            {
                waited += Time.unscaledDeltaTime;

                if (waited > PatiencePerScene)
                {
                    // 끝나지 않는 로드가 순회를 무한히 붙잡지 않게 한다.
                    Debug.LogWarning("[Unity Play MCP] Scene " + buildIndex + " did not finish loading in " +
                                     PatiencePerScene + "s. Moving on.");
                    yield break;
                }

                yield return null;
            }

            for (var frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }
        }

        private void Finish()
        {
            _walking = null;
            Destroy(gameObject);
        }
    }
}
