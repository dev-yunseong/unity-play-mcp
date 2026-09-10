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
    /// play 중 assembly reload 를 건넌 host 가 자기 runtime 과 transport 를 다시 세우는지.
    /// </summary>
    /// <remarks>
    /// edit mode 로 내려올 수 없다. 여기서 보는 것이 <c>Awake</c>, <c>OnEnable</c>, <c>Update</c> 가 서로에게
    /// 무엇을 남기는가이고, 그 셋은 play mode 밖에서 돌지 않는다.
    ///
    /// 실패는 대개 assert 가 아니라 log 로 온다. Unity Test Framework 는 test 가 도는 동안 올라온 예외를
    /// 그대로 실패로 치므로, reload 뒤 <c>Update</c> 가 <c>RecordFrameTime</c> 에서 던지면 — issue #57 이 그것이다 —
    /// 프레임을 넘기는 것만으로 이 fixture 가 붉어진다.
    /// </remarks>
    public sealed class HostReloadRecoveryTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject host;

        /// <summary>이 fixture 가 건드리는 유일한 전역. project 의 값을 그대로 돌려주려고 적어 둔다.</summary>
        private bool projectRunInBackground;

        [SetUp]
        public void SetUp()
        {
            projectRunInBackground = Application.runInBackground;

            // 다른 fixture 가 남긴 host 는 port 17311 을 쥐고 있다. 그것을 그대로 두면 여기서 세운 host 가
            // server 를 열지 못한다.
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
            // Start 가 지나가야 host 가 reload 를 건널 자격을 갖춘다.
            yield return null;

            AssemblyReloadSimulation.Rehearse(manager);

            // 예전에는 여기서 매 프레임 NullReferenceException 이 올라왔다.
            yield return null;
            yield return null;

            Assert.That(FrameTimes(manager), Is.Not.Null,
                "reload 뒤 EnsureRuntime 이 다시 돌지 않으면 Update 가 매 프레임 던진다.");
        }

        /// <remarks>
        /// scene 이 host 를 꺼진 채로 들고 오면 <c>Awake</c> 는 그때 돌고 <c>Start</c> 는 게임이 켤 때까지
        /// 오지 않는다. 그 사이의 reload 는 <c>Awake</c> 가 만든 것을 지우는데, 그때 <c>OnEnable</c> 은
        /// <c>hasStarted</c> 가 아직 false 라 그냥 돌아간다. 다시 세울 자리가 <c>Start</c> 말고 없다.
        /// </remarks>
        [UnityTest]
        public IEnumerator RebuildsItsRuntimeWhenTheReloadLandsBeforeStart()
        {
            var manager = CreateHost();
            manager.enabled = false;

            AssemblyReloadSimulation.Rehearse(manager);

            // Start 가 이 프레임에 온다.
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

            // start_readings 가 15초 timeout 대신 답을 주는지가 issue #57 의 두 번째 acceptance criterion 이다.
            Assert.That(manager.StartReadings(), Is.True);
            Assert.That(manager.Reading, Is.True);

            manager.StopReadings();
        }

        /// <remarks>
        /// reload 는 static 도 지우므로, 되살아난 host 는 자기가 살아 있는 하나라는 표시를 잃은 채로 깨어난다.
        /// 그 표시를 되찾지 못하면 다음에 로드된 scene 의 host 가 빈 자리를 차지해 두 host 가 같은 port 를
        /// 두고 다툰다.
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
        /// domain reload 를 끄고 play mode 에 드는 project 에서는 static 이 play 세션을 건너 살아남아, 지난
        /// 세션에 파괴된 host 가 자리에 남는다. <c>instance != null</c> 이 Unity 의 비교라서 그 자리는 비어
        /// 있는 것으로 읽히고, 새 host 가 그것을 차지한다.
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
        /// <c>RecordFrameTime</c> 의 문서가 약속하는 것은 "전송 상태와 무관하게 매 프레임" 이다. 그것을 지키는
        /// 것이 <c>Update</c> 안에서의 위치이고, transport 를 다루는 자리 안쪽으로 들어가는 순간 아무도 연결하지
        /// 않은 세션의 프레임타임이 통째로 비어 버린다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsRecordingFrameTimesWithNoClientConnected()
        {
            var manager = CreateHost();
            yield return null;

            Assert.That(manager.TransportOpen, Is.True, "server 는 서 있고, 붙은 client 는 없다.");

            // 프레임을 세지 않고 몇 번 물어본다. 첫 프레임은 씬 로드 시간이 실려 있어 recorder 가 버리고,
            // host 의 성능 보고도 1초에 한 번 같은 창을 가져가므로 특정 프레임 수를 못 박으면 흔들린다.
            var recorded = false;
            for (var attempt = 0; attempt < 10 && !recorded; attempt++)
            {
                yield return null;
                recorded = FrameTimes(manager).TrySummarize(1f / 60f, out _);
            }

            Assert.That(recorded, Is.True, "client 가 없어도 프레임타임은 쌓여야 한다.");
        }

        /// <remarks>
        /// reload 가 아니라 그냥 껐다 켜는 길. <c>OnEnable</c> 을 통째로 바꾼 변경이라 이쪽이 예전처럼 도는지를
        /// 함께 지킨다.
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
        /// issue #57 의 Constraints 가 짚은 자리다. 예전에는 <c>RecordFrameTime</c> 이 <c>Update</c> 의 첫
        /// 줄이라 거기서 던지면 그 프레임의 입력 전진도 요청 처리도 통째로 사라졌다. 지표 하나를 잃는 것과
        /// 원격 제어 전체를 잃는 것은 값이 다르다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsHandlingRequestsWhenFrameTimeRecordingThrows()
        {
            var manager = CreateHost();
            yield return null;

            // transport 를 다룬 흔적을 지우고, 진단 수집만 부러뜨린다.
            Set(manager, "transportWasConnected", false);
            Set(manager, "frameTimeRecorder", null);

            // 성능 보고도 recorder 를 읽는다. 그쪽 문을 명시적으로 닫아, 이번 프레임에 던지는 자리가
            // RecordFrameTime 하나임을 우연이 아니라 약속으로 쥔다.
            Set(manager, "nextPerformanceReportTime", Time.unscaledTime + 60f);

            // 예외는 삼키지 않는다. 삼켰다면 이 결함이 로그에 남지 않아 아무도 찾지 못했을 것이다.
            LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));
            yield return null;

            // 다음 프레임이 또 던지지 않도록 바로 되돌린다.
            Set(manager, "frameTimeRecorder", new FrameTimeRecorder());

            Assert.That(NoticedTheTransport(manager), Is.True,
                "성능 수집이 던져도 그 프레임의 요청 처리는 이미 지나갔어야 한다.");
        }

        /// <remarks>
        /// server 가 열려 있는 동안만 <c>Application.runInBackground</c> 를 빌리고, 내려갈 때 게임의 값을
        /// 돌려준다. reload 는 그 사이를 지나가므로 빌린 값을 게임의 값으로 착각할 위험이 여기에 있다 —
        /// 착각하면 게임은 제가 꺼 둔 설정을 영영 돌려받지 못한다.
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
        /// reload 직전에 실제로 도는 것은 <c>OnDisable</c> 이고, 그것이 잡고 있던 입력을 놓는다. 손을 뗀 적 없는
        /// 키를 게임에 남긴 채 domain 이 내려가면 게임은 영영 오지 않을 key up 을 기다린다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ReleasesHeldInputWhenItGoesDown()
        {
            var manager = CreateHost();
            yield return null;

            VirtualInput.PressKey(KeyCode.A);

            // 누름은 다음 프레임부터 눌린 것으로 읽힌다. 폴링하는 쪽이 script 실행 순서와 무관하게 그것을
            // 보게 하려고 VirtualKeyboardState 가 그렇게 정해 두었고, 놓는 것도 같은 규칙을 따른다.
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
        /// host 가 쥔 <see cref="FrameTimeRecorder"/>. reload 뒤 이것이 다시 만들어졌는지가 issue #57 의
        /// 전부라서, test 가 그것을 직접 붙잡는다.
        /// </summary>
        private static FrameTimeRecorder FrameTimes(UnityPlayMcpHost manager)
        {
            return (FrameTimeRecorder)Field("frameTimeRecorder").GetValue(manager);
        }

        /// <summary>
        /// 이번 프레임에 <c>PumpTransport</c> 가 돌았는지. <c>NoticeNewConnection</c> 이 거기서만
        /// 이 표시를 쓰므로, 그 자리까지 내려갔다는 증거가 된다.
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
