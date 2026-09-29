using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 낡은 상태를 의심하는 독자에게 host 가 전량 reading 을 보내는지 (#69).
    /// </summary>
    /// <remarks>
    /// 두 자리다. 새 client 가 붙었을 때 — 소켓은 client 가 없어도 보내기에 실패하지 않아 <c>Pulse</c> 가 전달 실패를 모르므로 —
    /// 와, 이미 도는 채널에 <c>start_readings</c> 가 다시 왔을 때.
    ///
    /// 진짜 server 대신 문서를 받아 적는 transport 를 끼운다. 진짜 server 에 붙는 client 를 test 안에서 세우지 않고도 "client 가
    /// 하나 더 열렸다" 를 말할 수 있어야 해서다. 끼우기 전에 진짜 server 를 먼저 닫는다 — 그러지 않으면 port 17311 이 다음
    /// fixture 까지 잡혀 있다.
    /// </remarks>
    public sealed class ReadingRecoveryTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class RecordingTransport : IAgentTransport
        {
            internal readonly List<string> Sent = new List<string>();

            public bool IsConnected { get { return true; } }

            public int ClientsOpened { get; set; }

            public void Start() { }

            public void Stop() { }

            public bool TryDequeueMessage(out AgentMessage message)
            {
                message = null;
                return false;
            }

            public void Send(string text) { Sent.Add(text); }

            public void Dispose() { }

            internal List<string> Pulses()
            {
                return Sent.Where(text => text.StartsWith("{\"type\":\"PULSE\"")).ToList();
            }
        }

        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            ClearHosts();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            ClearHosts();
        }

        [UnityTest]
        public IEnumerator 새_client_가_붙으면_도는_채널이_전량_reading_을_보낸다()
        {
            var manager = CreateHost();
            yield return null;
            var transport = SwapInRecordingTransport(manager);

            Assert.That(manager.StartReadings(), Is.True);
            yield return Settled(transport);
            var before = transport.Pulses().Count;

            transport.ClientsOpened = 1;

            yield return WaitForWhole(transport, before);
            manager.StopReadings();
        }

        [UnityTest]
        public IEnumerator 이미_도는_채널에_다시_온_start_readings_는_전량_reading_을_보낸다()
        {
            var manager = CreateHost();
            yield return null;
            var transport = SwapInRecordingTransport(manager);

            Assert.That(manager.StartReadings(), Is.True);
            yield return Settled(transport);
            var before = transport.Pulses().Count;

            Assert.That(manager.StartReadings(), Is.True);

            yield return WaitForWhole(transport, before);
            manager.StopReadings();
        }

        private UnityPlayMcpHost CreateHost()
        {
            host = new GameObject("Unity Play MCP reading recovery test");
            return host.AddComponent<UnityPlayMcpHost>();
        }

        /// <summary><c>Start</c> 가 연 진짜 server 를 닫고 그 자리에 받아 적는 transport 를 끼운다.</summary>
        private static RecordingTransport SwapInRecordingTransport(UnityPlayMcpHost manager)
        {
            var field = typeof(UnityPlayMcpHost).GetField("webSocketTransport", PrivateInstance);
            var real = (IAgentTransport)field.GetValue(manager);
            Assert.That(real, Is.Not.Null, "Start 가 transport 를 세웠어야 한다.");

            real.Stop();
            real.Dispose();

            var recording = new RecordingTransport();
            field.SetValue(manager, recording);
            return recording;
        }

        /// <summary>첫 전량 reading 이 건네지고, 그 뒤로 박자가 한 번 더 지나갈 때까지.</summary>
        private static IEnumerator Settled(RecordingTransport transport)
        {
            yield return WaitUntil(() => transport.Pulses().Count > 0, 5f, "the first reading never left");
            yield return new WaitForSecondsRealtime(1.5f);
        }

        private static IEnumerator WaitForWhole(RecordingTransport transport, int before)
        {
            yield return WaitUntil(
                () => transport.Pulses().Skip(before).Any(pulse => pulse.Contains("\"whole\":true")),
                5f,
                "no whole reading followed");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string failure)
        {
            var deadline = Time.realtimeSinceStartup + seconds;

            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, failure);
        }

        private static void ClearHosts()
        {
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }
    }
}
