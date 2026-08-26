using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 리포트가 화면이 아니라 게임을 덮도록 빌드의 모든 씬을 방문한다.
    /// </summary>
    /// <remarks>
    /// 화면에 닿기까지 플레이해 가는 것이 그것을 서술하는 정직한 방법이지만, 모든 게임의 모든 씬에 대해 청할 수 있는 일은
    /// 아니다. 이것은 로딩 자체를 몰고 간다: 빌드 설정의 각 씬을 차례로 띄우고, <c>Awake</c> 와 <c>Start</c> 가 돌 짬을
    /// 주고, 읽고, 뒤에 남긴다.
    ///
    /// 언제나 일부러만 시작한다. 화면에 있는 것을 갈아치우고 진행 중이던 실행을 버리므로, 일어나는 일이 아니라 청하는
    /// 일이다. 곁에 더하지 않고 홀로 로드한다: 도중에 멎은 순회가 게임 위에 아무것도 얹어 두지 않도록 — 이전 판이 실패한
    /// 방식이 그것이다.
    /// </remarks>
    internal sealed class SceneWalk : MonoBehaviour
    {
        /// <summary>씬을 읽기 전에 주는 프레임 수.</summary>
        /// <remarks>
        /// 로드가 끝나는 데 하나, 첫 <c>Update</c> 에 하나. 그 지점이면 <c>Awake</c> 와 <c>Start</c> 가 돌았고 라벨은 씬과
        /// 함께 저장된 자리표시자가 아니라 게임이 넣은 텍스트를 보여 준다.
        /// </remarks>
        private const int SettleFrames = 2;

        /// <summary>순회가 넘어가기 전까지 씬 하나가 올라오는 데 걸릴 수 있는 초.</summary>
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

            var carrier = new GameObject("Artel Scene Walk") { hideFlags = HideFlags.HideAndDontSave };
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
                // 예전에는 똑같이 읽히던 서로 다른 두 사실. Build Settings 가 비어 있고 제 주소를 나열할 방법도 없는 프로젝트가
                // 씬이 없는 프로젝트인 것은 아니다.
                Debug.LogWarning(ExtraScenes.Available
                    ? "[Artel] No scenes in Build Settings and none reachable by address."
                    : "[Artel] No scenes in Build Settings. If this game loads its scenes by " +
                      "address, install Addressables support and walk again.");

                AffordanceReport.Merge("(walk)", string.Empty, new List<string>
                {
                    ExtraScenes.Available ? "no-scenes-anywhere" : "no-scenes-in-build-settings"
                });

                Finish();
                yield break;
            }

            // 게임을 되돌려 놓을 자리. 씬은 다른 폴더의 다른 씬과 이름을 나눠 가질 수 있으므로 빌드 인덱스로 잡는다.
            var origin = SceneManager.GetActiveScene().buildIndex;

            for (var index = 0; index < count; index++)
            {
                Debug.Log("[Artel] Walking scene " + (index + 1) + " of " + count + ".");
                strays.Capture();
                yield return Read(index);
            }

            for (var index = 0; index < addressed.Count; index++)
            {
                Debug.Log("[Artel] Walking addressed scene " + (index + 1) + " of " + addressed.Count + ".");
                strays.Capture();
                yield return Read(addressed[index]);
            }

            if (origin >= 0)
            {
                yield return Load(origin);
            }

            // 마지막에, 한 번 읽는다. 게임이 씬 로드를 건너 쥐고 있던 것은 순회가 끝날 무렵까지 쌓인 무엇이고, 이 carrier 가
            // 바로 그 같은 씬에 산다 — 스스로 설치되는 패키지가 그 씬에 대해 가진 유일한 손잡이다.
            SceneEvidenceScan.CapturePersistent(gameObject.scene);

            Debug.Log("[Artel] Walk finished. " + AffordanceReport.SceneCount + " scenes in the report; removed " +
                      removed + " object(s) left behind: " + AffordanceBootstrap.Save());

            Finish();
        }

        /// <summary>빌드 설정에 없는 씬들의 주소, 또는 없음.</summary>
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
                Debug.LogWarning("[Artel] The address catalogue could not be read: " + exception.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// 주소로 된 씬 하나를 홀로 띄우고 읽는다.
        /// </summary>
        /// <remarks>
        /// 홀로는 게임이 그것을 플레이하는 방식이 아니다. 주소로 로드되는 씬은 대개 이미 올라와 있는 매니저 씬 위에 더해지도록
        /// 만들어졌고, 혼자 올라온 것은 반쯤 지어진 채로 오거나 비어 있을 수 있다. 그래서 판독은 버리지 않고 쥐되 표시해 둔다 —
        /// 불완전한 로드에서 서술된 화면도, 아무도 그것을 전체인 양 읽지만 않는다면 그 화면에 대한 유일한 진술이다.
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
                Debug.LogWarning("[Artel] " + address + " would not load: " + exception.Message);
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

            // 판독을 뜬 바로 그 자리에서 화면도 남긴다. 다음 씬 로드가 시작되면 back buffer 는 이미 다른 씬이다.
            // Collect 앞이다 — 저쪽은 앞선 씬이 남긴 것을 이 씬으로 옮겨 붙이는 일이고, 화면은 그 전의 이 씬이다.
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
                // 게임이 놓인 자리에 머물기를 거부했다 — 스스로 다음으로 넘어가는 타이틀 화면이거나, 올라오지 못한 씬이다. 그것이
                // 착지한 무엇의 이름으로 기록하는 대신 그렇다고 말한다.
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

        /// <summary>방문 씬이 DDOL 씬으로 빼낸 새 root를 다시 방문 씬에 붙인다.</summary>
        /// <remarks>
        /// 다음 <c>Single</c> 로드가 방문 씬을 버릴 때 함께 파괴된다. <c>Destroy</c>를 바로 부르면
        /// 다음 씬의 <c>Awake</c>보다 늦게 파괴돼 singleton 중복 검사를 오염시킬 수 있다.
        /// </remarks>
        private void Collect(Scene scene, string identity)
        {
            var moved = strays.MoveInto(scene);
            removed += moved.Count;

            if (moved.Count > 0)
            {
                Debug.Log(
                    "[Artel] Scene walk will unload " + moved.Count + " object(s) left behind by " +
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
                Debug.LogWarning("[Artel] Scene " + buildIndex + " would not load: " + exception.Message);
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
                    // 가둔다. 그러지 않으면 끝나지 않는 씬이 에디터가 열려 있는 내내 순회를 붙잡고, 그때쯤이면 게임은 이미 쓸 수 없다.
                    Debug.LogWarning("[Artel] Scene " + buildIndex + " did not finish loading in " +
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
