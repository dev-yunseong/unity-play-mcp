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
    /// 오래된 상태를 의심하는 읽는 쪽에 host 가 whole reading 을 보내는지 확인한다 (#69).
    /// </summary>
    /// <remarks>
    /// 두 경우다. 새 client 가 붙었을 때 (socket 은 client 가 없어도 전송 실패를 알리지 않는다) 와, 이미 도는
    /// channel 에 <c>start_readings</c> 가 다시 왔을 때다.
    /// 실제 server 대신 문서를 기록하는 transport 를 쓴다. 교체 전에 실제 server 를 닫아야 port 17311 이
    /// 다음 fixture 까지 잡혀 있지 않는다.
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

        /// <summary><c>Start</c> 가 연 실제 server 를 닫고 기록용 transport 로 교체한다.</summary>
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

        /// <summary>첫 whole reading 이 전달되고 `pulse` 간격이 한 번 더 지날 때까지 기다린다.</summary>
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
