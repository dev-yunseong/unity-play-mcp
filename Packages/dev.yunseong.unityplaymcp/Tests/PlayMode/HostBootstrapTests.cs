using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <remarks>
    /// scene 에 host 가 없는 build 에도 host 가 생기는지 확인한다.
    /// 다른 fixture 가 SetUp 에서 host 를 파괴하므로 <c>AfterSceneLoad</c> hook 결과를 찾지 않고 hook 메서드를
    /// 직접 부른다. 그래야 fixture 실행 순서에 의존하지 않는다. hook 등록은 attribute 로 따로 확인한다.
    /// </remarks>
    public sealed class HostBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            // 다른 fixture 가 남긴 host 를 먼저 비운다.
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
            // scene 에 있던 host 는 설정을 담고 있을 수 있으므로 대체하면 안 된다.
            var carried = new GameObject("Carried By The Scene").AddComponent<UnityPlayMcpHost>();

            UnityPlayMcpHost.SpawnInDevelopmentBuilds();

            var hosts = Object.FindObjectsOfType<UnityPlayMcpHost>(true);
            Assert.AreEqual(1, hosts.Length, "The spawn must not add a second host.");
            Assert.AreSame(carried, hosts.Single());
        }

        /// <remarks>
        /// 위 test 들은 메서드를 직접 부르므로 hook 등록이 사라져도 통과한다. 등록은 여기서 확인한다.
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
