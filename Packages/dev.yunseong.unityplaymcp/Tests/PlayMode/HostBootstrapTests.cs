using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <remarks>
    /// scene 이 host 를 하나도 들고 있지 않은 build 에 QA 를 붙일 수 있는지가 여기서 지키는 것이다.
    ///
    /// 예전에는 <c>AfterSceneLoad</c> hook 이 남긴 오브젝트를 나중에 찾아보는 방식이었다. 그 hook 은
    /// play mode 당 한 번만 돌고 <see cref="MouseMessageActionTests"/> 와 <see cref="PointerActionTests"/>
    /// 는 SetUp 에서 살아 있는 host 를 전부 파괴하므로 — port 17311 을 자기 host 에게 넘겨야 한다 —
    /// 이 fixture 가 그 둘보다 먼저 돌아야만 통과했다. 실행 순서는 fixture 이름의 알파벳 순이라,
    /// 이름을 바꾸는 것만으로 조용히 깨지고 무엇이 깨졌는지는 이름과 아무 상관이 없어 보였다.
    /// 그래서 hook 이 부르는 메서드를 직접 부른다. 순서에 기대지 않고, 등록이 살아 있는지는 attribute
    /// 로 따로 확인한다.
    /// </remarks>
    public sealed class HostBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            // 다른 fixture 가 남긴 host 를 보고 판단하지 않기 위해 먼저 비운다.
            ClearHosts();
        }

        [TearDown]
        public void TearDown()
        {
            // 여기서 만든 host 가 port 17311 을 쥔 채 다음 fixture 로 넘어가지 않게 한다.
            ClearHosts();
        }

        private static void ClearHosts()
        {
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        private static MethodInfo SpawnMethod()
        {
            return typeof(UnityPlayMcpHost).GetMethod(
                nameof(UnityPlayMcpHost.SpawnInDevelopmentBuilds),
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        [Test]
        public void SpawnsAHostWhenNoSceneCarriesOne()
        {
            UnityPlayMcpHost.SpawnInDevelopmentBuilds();

            var spawned = GameObject.Find("Unity Play MCP");
            Assert.IsNotNull(spawned, "A development build has to spawn the host itself.");
            Assert.IsNotNull(spawned.GetComponent<UnityPlayMcpHost>());
        }

        [Test]
        public void LeavesTheHostTheSceneAlreadyCarries()
        {
            // scene 이 들고 온 host 는 설정을 담고 있을 수 있다. 그것을 밀어내면 안 된다.
            var carried = new GameObject("Carried By The Scene").AddComponent<UnityPlayMcpHost>();

            UnityPlayMcpHost.SpawnInDevelopmentBuilds();

            var hosts = Object.FindObjectsOfType<UnityPlayMcpHost>(true);
            Assert.AreEqual(1, hosts.Length, "The spawn must not add a second host.");
            Assert.AreSame(carried, hosts.Single());
        }

        /// <remarks>
        /// 위 두 test 는 메서드를 직접 부르므로 hook 등록이 사라져도 통과한다. 등록이야말로 이 기능이
        /// 실제로 도는 유일한 이유이므로 따로 지킨다.
        /// </remarks>
        [Test]
        public void RunsItselfAfterTheFirstSceneLoads()
        {
            var method = SpawnMethod();
            Assert.IsNotNull(method, "SpawnInDevelopmentBuilds must stay reachable for the hook.");

            var hook = method
                .GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false)
                .Cast<RuntimeInitializeOnLoadMethodAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(hook, "Without the attribute nothing ever calls this.");
            Assert.AreEqual(RuntimeInitializeLoadType.AfterSceneLoad, hook.loadType,
                "Running before the scene loads would push aside a host the scene carries.");
        }
    }
}
