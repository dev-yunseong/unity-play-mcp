using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// 전송을 누가 설치하느냐가 컴포넌트 실행 순서에 걸리지 않는다.
    /// </summary>
    /// <remarks>
    /// 유니티는 컴포넌트 사이의 <c>Awake</c>·<c>OnEnable</c> 순서를 보장하지 않는데,
    /// <c>ArtelTestPageManager</c> 는 자기 <c>OnEnable</c> 에서 매니저에 전송을 꽂는다. 그래서 같은 씬이
    /// 실행마다 두 갈래로 갈렸다 — 매니저가 먼저 돌면 자기 소켓을 열어 오케스트레이션에 붙고 테스트
    /// 페이지는 물러났고, 테스트 페이지가 먼저 돌면 아직 없는 <c>sceneStatePoller</c> 를 건드려
    /// <c>NullReferenceException</c> 이 났다. 그 예외는 전송 필드를 이미 채운 뒤에 터져서, 게임은 어느
    /// 쪽에도 붙지 못하고 테스트 페이지 서버도 뜨지 않은 채로 남았다.
    ///
    /// 살아 있는 매니저가 필요해 플레이 모드에서만 돈다. <c>Awake</c> 가 부르는
    /// <c>DontDestroyOnLoad</c> 는 에디터 스크립트에서 부를 수 없다.
    /// </remarks>
    public sealed class TransportOrderingTests
    {
        private GameObject host;
        private ArtelManager displacedInstance;

        [SetUp]
        public void SetUp()
        {
            displacedInstance = ArtelManagerSlot.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            ArtelManagerSlot.Restore(displacedInstance);
        }

        /// <summary>
        /// 매니저의 <c>Awake</c> 보다 먼저 도착한 전송도 그대로 설치된다.
        /// </summary>
        /// <remarks>
        /// 비활성 오브젝트에 붙인 컴포넌트는 활성화 전까지 <c>Awake</c> 를 받지 않는다. 실제 씬에서
        /// 테스트 페이지가 매니저를 앞질렀을 때와 같은 상태이고, 고치기 전에는 여기서 터졌다.
        /// </remarks>
        [Test]
        public void Transport_Installs_WhenItArrivesBeforeAwake()
        {
            host = new GameObject("Artel transport ordering test");
            host.SetActive(false);

            var manager = host.AddComponent<ArtelManager>();
            var transport = new SilentTransport();

            Assert.DoesNotThrow(() => manager.SetWebSocketTransport(transport, false));
            Assert.That(manager.HasWebSocketTransport, Is.True);

            host.SetActive(true);

            // Awake 가 뒤늦게 돌아도 이미 선 전송을 밀어내지 않는다.
            Assert.That(manager.HasWebSocketTransport, Is.True);
            Assert.That(OwnsTransport(manager), Is.False);
        }

        /// <summary>
        /// 첫 연결은 <c>Start</c> 의 몫이라, <c>OnEnable</c> 에서 꽂힌 전송이 항상 앞선다.
        /// </summary>
        /// <remarks>
        /// 유니티가 보장하는 것은 모든 <c>OnEnable</c> 이 어떤 <c>Start</c> 보다 먼저 끝난다는 것뿐이다.
        /// 그래서 매니저가 어디에 붙을지 정하는 자리는 <c>Start</c> 하나뿐이고, 그 앞에서는 어떤 순서로
        /// 돌든 주입이 도착해 있다.
        /// </remarks>
        [UnityTest]
        public IEnumerator FirstConnection_WaitsForStart_SoAnInjectedTransportWins()
        {
            host = new GameObject("Artel transport ordering test");
            host.SetActive(false);

            var manager = host.AddComponent<ArtelManager>();
            SetConnectOnStart(manager, true);

            // 매니저 뒤에 붙였으므로 이 컴포넌트의 OnEnable 은 매니저의 것보다 늦게 돈다. 고치기 전이라면
            // 매니저가 이미 자기 연결을 정한 뒤에 도착하는 순서다.
            var injector = host.AddComponent<TransportInjector>();
            injector.Manager = manager;
            injector.Transport = new SilentTransport();

            host.SetActive(true);

            // 모든 OnEnable 이 끝났고 Start 는 아직이다. 매니저가 연결처를 정하기 전에 주입이 도착해 있다.
            Assert.That(injector.InstalledWhileManagerHadNone, Is.True);
            Assert.That(HasStarted(manager), Is.False);

            yield return null;

            Assert.That(HasStarted(manager), Is.True);

            // 자기 소켓을 열지 않고 꽂힌 전송에 양보했다. ownsTransport 가 곧 "이 게임은
            // 오케스트레이션에 직접 붙지 않는다" 는 뜻이다.
            Assert.That(OwnsTransport(manager), Is.False);
            Assert.That(manager.HasWebSocketTransport, Is.True);
        }

        private static bool OwnsTransport(ArtelManager manager)
        {
            return (bool)Field("ownsTransport").GetValue(manager);
        }

        private static bool HasStarted(ArtelManager manager)
        {
            return (bool)Field("hasStarted").GetValue(manager);
        }

        private static void SetConnectOnStart(ArtelManager manager, bool value)
        {
            Field("connectOnStart").SetValue(manager, value);
        }

        private static FieldInfo Field(string name)
        {
            var field = typeof(ArtelManager)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ArtelManager." + name + " is gone.");
            return field;
        }

        /// <summary>
        /// <c>ArtelTestPageManager</c> 가 하는 일 중 순서에 걸리는 부분만 흉내낸다. 진짜를 쓰지 않는 것은
        /// 그쪽이 HTTP·WebSocket 서버를 실제 포트에 띄우기 때문이다.
        /// </summary>
        private sealed class TransportInjector : MonoBehaviour
        {
            public ArtelManager Manager;
            public IArtelWebSocketTransport Transport;

            /// <summary>주입 시점에 매니저가 아직 전송을 갖고 있지 않았는가.</summary>
            public bool InstalledWhileManagerHadNone;

            private void OnEnable()
            {
                InstalledWhileManagerHadNone = !Manager.HasWebSocketTransport;
                Manager.SetWebSocketTransport(Transport, false);
            }
        }

        private sealed class SilentTransport : IArtelWebSocketTransport
        {
            public List<string> Sent { get; } = new List<string>();

            public bool IsConnected { get { return true; } }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                message = null;
                return false;
            }

            public void Send(string text)
            {
                Sent.Add(text);
            }

            public void Dispose()
            {
            }
        }
    }
}
