using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityPlayMcp.Diagnostics;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 뒤 host 가 runtime 과 transport 를 다시 세우는지 확인한다.
    /// </summary>
    /// <remarks>
    /// <c>Awake</c>, <c>OnEnable</c>, <c>Update</c> 순서에 의존하므로 play mode 에서만 돈다.
    /// reload 뒤 <c>Update</c> 가 던지는 예외는 log 로 올라와 test 실패가 된다 (#57).
    /// </remarks>
    public sealed class HostReloadRecoveryTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject host;

        /// <summary>fixture 가 바꾸는 전역 값이다. TearDown 에서 project 값으로 되돌린다.</summary>
        private bool projectRunInBackground;

        [SetUp]
        public void SetUp()
        {
            projectRunInBackground = Application.runInBackground;

            // 다른 fixture 가 남긴 host 가 port 17311 을 쥐고 있으면 새 host 가 server 를 열지 못한다.
            ClearHosts();
        }

        [TearDown]
        public void TearDown()
        {
            // 실패한 test 가 눌린 키를 다음 fixture 로 넘기지 않게 한다.
            VirtualInput.ReleaseAllVirtualInput();

            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            ClearHosts();
            Application.runInBackground = projectRunInBackground;
        }

        private static void ClearHosts()
        {
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator RebuildsItsRuntimeAfterAnAssemblyReload()
        {
            var manager = CreateHost();
            // Start 가 지나야 host 가 reload 를 건널 수 있다.
            yield return null;

            AssemblyReloadSimulation.Rehearse(manager);

            yield return null;
            yield return null;

            Assert.That(FrameTimes(manager), Is.Not.Null,
                "reload 뒤 EnsureRuntime 이 다시 돌지 않으면 Update 가 매 프레임 던진다.");
        }

        /// <remarks>
        /// host 가 꺼진 채 로드되면 <c>Awake</c> 뒤 <c>Start</c> 전에 reload 가 올 수 있다.
        /// 이때 <c>OnEnable</c> 은 <c>hasStarted</c> 가 false 라 돌아가므로 <c>Start</c> 가 runtime 을 다시 세워야 한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator RebuildsItsRuntimeWhenTheReloadLandsBeforeStart()
        {
            var manager = CreateHost();
            manager.enabled = false;

            AssemblyReloadSimulation.Rehearse(manager);

            yield return null;
            yield return null;

            Assert.That(FrameTimes(manager), Is.Not.Null, "Start 도 runtime 을 다시 세워야 한다.");
            Assert.That(manager.TransportOpen, Is.True);
        }

        [UnityTest]
        public IEnumerator ReopensTheTransportAfterAnAssemblyReload()
        {
            var manager = CreateHost();
            yield return null;
            Assert.That(manager.TransportOpen, Is.True, "Start 가 server 를 세웠어야 한다.");

            AssemblyReloadSimulation.Rehearse(manager);
            yield return null;

            Assert.That(manager.TransportOpen, Is.True,
                "reload 는 transport 를 지운다. 그것을 다시 세우는 것은 OnEnable 이 부르는 BeginHosting 이다.");
        }

        [UnityTest]
        public IEnumerator AnswersStartReadingsAfterAnAssemblyReload()
        {
            var manager = CreateHost();
            yield return null;

            AssemblyReloadSimulation.Rehearse(manager);
            yield return null;

            // start_readings 가 timeout 없이 답해야 한다 (#57).
            Assert.That(manager.StartReadings(), Is.True);
            Assert.That(manager.Reading, Is.True);

            manager.StopReadings();
        }

        /// <remarks>
        /// reload 는 static slot 도 지운다. 되살아난 host 가 slot 을 되찾지 못하면 다음 scene 의 host 와
        /// 같은 port 를 두고 다툰다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsTheRecoveredHostWhenASecondOneAppears()
        {
            var manager = CreateHost();
            yield return null;

            AssemblyReloadSimulation.Rehearse(manager);
            yield return null;

            var newcomer = new GameObject("Unity Play MCP newcomer").AddComponent<UnityPlayMcpHost>();
            // Destroy 는 프레임 끝에 처리된다.
            yield return null;

            Assert.That(newcomer == null, Is.True, "두 번째 host 는 스스로 물러나야 한다.");
            Assert.That(manager.TransportOpen, Is.True, "먼저 있던 host 가 server 를 계속 쥔다.");
        }

        /// <remarks>
        /// domain reload 를 끈 project 에서는 static slot 에 지난 세션의 파괴된 host 가 남는다.
        /// <c>instance != null</c> 은 Unity 비교라 이 slot 을 빈 것으로 보고 새 host 가 차지해야 한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ClaimsTheSlotWhenItStillHoldsADestroyedHost()
        {
            var previousSession = new GameObject("Unity Play MCP previous session")
                .AddComponent<UnityPlayMcpHost>();
            yield return null;

            Object.DestroyImmediate(previousSession.gameObject);
            UnityPlayMcpHostSlot.Restore(previousSession);

            var manager = CreateHost();
            yield return null;

            Assert.That(manager.TransportOpen, Is.True, "새 host 가 자리를 잡고 server 를 세워야 한다.");
        }

        /// <remarks>
        /// <c>RecordFrameTime</c> 은 transport 상태와 무관하게 매 프레임 돌아야 한다. transport 처리 안쪽으로
        /// 옮기면 client 가 없는 세션의 frame time 이 비게 된다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsRecordingFrameTimesWithNoClientConnected()
        {
            var manager = CreateHost();
            yield return null;

            Assert.That(manager.TransportOpen, Is.True, "server 는 서 있고, 붙은 client 는 없다.");

            // 특정 프레임 수를 고정하지 않는다. recorder 는 첫 프레임을 버리고 성능 보고도 1초마다
            // 같은 창을 가져가므로 프레임 수를 못 박으면 흔들린다.
            var recorded = false;
            for (var attempt = 0; attempt < 10 && !recorded; attempt++)
            {
                yield return null;
                recorded = FrameTimes(manager).TrySummarize(1f / 60f, out _);
            }

            Assert.That(recorded, Is.True, "client 가 없어도 프레임타임은 쌓여야 한다.");
        }

        /// <remarks>
        /// reload 가 아닌 단순 disable/enable 경로도 transport 와 runtime 을 유지하는지 확인한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ReopensTheTransportOnAPlainReEnable()
        {
            var manager = CreateHost();
            yield return null;

            manager.enabled = false;
            Assert.That(manager.TransportOpen, Is.False, "OnDisable 이 server 를 내렸어야 한다.");

            manager.enabled = true;
            yield return null;

            Assert.That(manager.TransportOpen, Is.True);
            Assert.That(FrameTimes(manager), Is.Not.Null, "재활성화가 runtime 을 버려서는 안 된다.");
        }

        /// <remarks>
        /// <c>RecordFrameTime</c> 이 던져도 그 프레임의 입력 진행과 요청 처리는 끝나 있어야 한다 (#57).
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsHandlingRequestsWhenFrameTimeRecordingThrows()
        {
            var manager = CreateHost();
            yield return null;

            // transport 표시를 지우고 진단 수집만 망가뜨린다.
            Set(manager, "transportWasConnected", false);
            Set(manager, "frameTimeRecorder", null);

            // 성능 보고도 recorder 를 읽으므로 미뤄서, 이번 프레임에 던지는 곳을 RecordFrameTime 하나로 고정한다.
            Set(manager, "nextPerformanceReportTime", Time.unscaledTime + 60f);

            // 예외는 삼키지 않아 log 에 남아야 한다.
            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));
            yield return null;

            // 다음 프레임이 또 던지지 않게 바로 되돌린다.
            Set(manager, "frameTimeRecorder", new FrameTimeRecorder());

            Assert.That(NoticedTheTransport(manager), Is.True,
                "성능 수집이 던져도 그 프레임의 요청 처리는 이미 지나갔어야 한다.");
        }

        /// <remarks>
        /// host 는 server 가 열린 동안만 <c>Application.runInBackground</c> 를 켜고 내려갈 때 게임 값을 돌려준다.
        /// reload 뒤 켜 둔 값을 게임 값으로 저장하면 게임 설정이 복구되지 않는다.
        /// </remarks>
        [UnityTest]
        public IEnumerator GivesTheGameItsRunInBackgroundBackAcrossAReload()
        {
            Application.runInBackground = false;

            var manager = CreateHost();
            yield return null;
            Assert.That(Application.runInBackground, Is.True, "server 가 열린 동안은 빌린다.");

            AssemblyReloadSimulation.Rehearse(manager);
            Assert.That(Application.runInBackground, Is.True, "reload 뒤에도 server 가 서므로 다시 빌린다.");

            manager.enabled = false;

            Assert.That(Application.runInBackground, Is.False,
                "reload 를 건너도 게임이 쥐고 있던 값을 잊지 않아야 한다.");
        }

        /// <remarks>
        /// reload 직전에는 <c>OnDisable</c> 이 돈다. 여기서 입력을 놓지 않으면 게임은 오지 않을 key up 을 기다린다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ReleasesHeldInputWhenItGoesDown()
        {
            var manager = CreateHost();
            yield return null;

            VirtualInput.PressKey(KeyCode.A);

            // VirtualKeyboardState 는 누름과 놓음을 다음 프레임부터 반영한다.
            yield return null;
            Assert.That(VirtualInput.GetKey(KeyCode.A), Is.True);

            manager.enabled = false;
            yield return null;

            Assert.That(VirtualInput.GetKey(KeyCode.A), Is.False,
                "OnDisable 이 잡고 있던 입력을 놓아야 한다.");
        }

        private UnityPlayMcpHost CreateHost()
        {
            host = new GameObject("Unity Play MCP reload test");
            return host.AddComponent<UnityPlayMcpHost>();
        }

        /// <summary>
        /// host 의 <see cref="FrameTimeRecorder"/> 를 읽는다. reload 뒤 다시 만들어졌는지 확인하는 데 쓴다 (#57).
        /// </summary>
        private static FrameTimeRecorder FrameTimes(UnityPlayMcpHost manager)
        {
            return (FrameTimeRecorder)Field("frameTimeRecorder").GetValue(manager);
        }

        /// <summary>
        /// 이번 프레임에 <c>PumpTransport</c> 가 돌았는지 반환한다. 이 필드는 <c>NoticeNewConnection</c> 만 쓴다.
        /// </summary>
        private static bool NoticedTheTransport(UnityPlayMcpHost manager)
        {
            return (bool)Field("transportWasConnected").GetValue(manager);
        }

        private static void Set(UnityPlayMcpHost manager, string field, object value)
        {
            Field(field).SetValue(manager, value);
        }

        private static FieldInfo Field(string name)
        {
            return typeof(UnityPlayMcpHost).GetField(name, PrivateInstance);
        }
    }
}
