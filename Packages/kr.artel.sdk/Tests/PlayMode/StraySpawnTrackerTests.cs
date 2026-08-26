using System.Collections;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// 씬 워크가 방문 씬이 남긴 오브젝트를 실제로 걷어내는지 본다. 플레이 모드에서만 돌 수 있다:
    /// <c>Awake</c>도 <c>DontDestroyOnLoad</c>도 에디터 스크립트에서는 동작하지 않는다.
    /// </summary>
    /// <remarks>
    /// Build Settings에 씬을 요구하지 않으려고 <see cref="AllSceneScanner"/>의 워크 전체가 아니라
    /// <see cref="StraySpawnTracker"/>만 직접 돌린다. 워크가 하는 일 중 이 클래스가 책임지는 부분
    /// — 방문 전 root를 기억했다가 그 뒤 생긴 root를 곧 죽을 씬으로 옮기는 것 — 이 그대로 재현된다.
    /// </remarks>
    public sealed class StraySpawnTrackerTests
    {
        /// <summary>
        /// <c>DontDestroyOnLoad</c>에 컴포넌트를 넘기는 쪽. WordVenture의 TutorialController가
        /// 쓰는 형태다.
        /// </summary>
        private sealed class ComponentPersistFixture : MonoBehaviour
        {
            private void Awake()
            {
                DontDestroyOnLoad(this);
            }
        }

        /// <summary>
        /// <c>DontDestroyOnLoad</c>에 GameObject를 넘기는 쪽. 같은 게임의 SaveLoadController와
        /// StageDataSingleton이 쓰는 형태이고, 두 형태의 결과가 갈리는지가 이 픽스처의 존재 이유다.
        /// </summary>
        private sealed class GameObjectPersistFixture : MonoBehaviour
        {
            private void Awake()
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        [UnityTest]
        public IEnumerator MoveInto_DoomsComponentPersistedRootSpawnedAfterCapture()
        {
            yield return AssertStrayIsDoomed(typeof(ComponentPersistFixture));
        }

        [UnityTest]
        public IEnumerator MoveInto_DoomsGameObjectPersistedRootSpawnedAfterCapture()
        {
            yield return AssertStrayIsDoomed(typeof(GameObjectPersistFixture));
        }

        /// <summary>
        /// 방문 전부터 있던 persistent 오브젝트는 게임의 것이므로 워크가 건드리면 안 된다.
        /// </summary>
        [UnityTest]
        public IEnumerator MoveInto_LeavesRootThatPersistedBeforeCapture()
        {
            var doomed = SceneManager.CreateScene("Artel Stray Test Doomed");
            var survivor = new GameObject("Preexisting Persistent", typeof(ComponentPersistFixture));

            var tracker = new StraySpawnTracker();
            tracker.Capture();

            var moved = tracker.MoveInto(doomed);

            CollectionAssert.IsEmpty(moved, "방문 전부터 있던 root를 옮겼다.");
            Assert.AreEqual(
                StraySpawnTracker.DontDestroyOnLoadScene(),
                survivor.scene,
                "방문 전부터 있던 root가 DontDestroyOnLoad 씬을 떠났다.");

            yield return SceneManager.UnloadSceneAsync(doomed);

            Assert.IsTrue(survivor != null, "방문 전부터 있던 root가 언로드에 휩쓸려 죽었다.");
            Object.DestroyImmediate(survivor);
        }

        /// <summary>
        /// 옮길 대상을 찾는 순회가 곧 죽을 씬 자신의 내용물까지 헤집지 않는지 본다. 그 씬은
        /// 어차피 통째로 언로드되므로 옮길 것이 없다.
        /// </summary>
        [UnityTest]
        public IEnumerator MoveInto_IgnoresContentsOfTheDoomedSceneItself()
        {
            var doomed = SceneManager.CreateScene("Artel Stray Test Doomed");

            var tracker = new StraySpawnTracker();
            tracker.Capture();

            var inside = new GameObject("Doomed Scene Root");
            SceneManager.MoveGameObjectToScene(inside, doomed);

            var moved = tracker.MoveInto(doomed);

            CollectionAssert.IsEmpty(moved, "곧 죽을 씬 자신의 root를 옮긴 것으로 셌다.");

            yield return SceneManager.UnloadSceneAsync(doomed);

            Assert.IsTrue(inside == null, "곧 죽을 씬의 root가 언로드에서 살아남았다.");
        }

        private static IEnumerator AssertStrayIsDoomed(System.Type fixtureType)
        {
            // 실제 워크에서 곧 언로드될 방문 씬 자리를 맡는다. Build Settings 없이 만들 수 있고
            // 언로드도 실제 씬과 똑같이 동작한다.
            var doomed = SceneManager.CreateScene("Artel Stray Test Doomed");

            var tracker = new StraySpawnTracker();
            tracker.Capture();

            // 방문 씬의 Awake가 하는 일과 같다. Capture 뒤에 생겨야 stray로 잡힌다.
            var stray = new GameObject("Stray " + fixtureType.Name, fixtureType);

            Assert.AreEqual(
                StraySpawnTracker.DontDestroyOnLoadScene(),
                stray.scene,
                fixtureType.Name + "이(가) DontDestroyOnLoad 씬으로 가지 않았다.");
            Assert.IsNull(
                stray.transform.parent,
                fixtureType.Name + "이(가) root가 아니다. root가 아니면 씬 사이를 옮길 수 없다.");

            var moved = tracker.MoveInto(doomed);

            CollectionAssert.AreEqual(new[] { stray.name }, moved, "stray를 옮긴 것으로 세지 않았다.");
            Assert.AreEqual(doomed, stray.scene, "stray가 곧 죽을 씬으로 옮겨지지 않았다.");

            yield return SceneManager.UnloadSceneAsync(doomed);

            Assert.IsTrue(stray == null, "stray가 언로드에서 살아남았다.");
        }
    }
}
