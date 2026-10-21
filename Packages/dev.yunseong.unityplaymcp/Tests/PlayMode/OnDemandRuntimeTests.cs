using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Tests
{
    public sealed class OnDemandRuntimeTests
    {
        private GameObject hostObject;
        private bool background;
        private float timeScale;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Transport : IAgentTransport
        {
            public bool IsConnected => true;
            public int ActiveClients { get; set; }
            public int IdleGeneration { get; set; }
            public int ClientsOpened { get; set; }
            public readonly Queue<AgentMessage> Messages = new Queue<AgentMessage>();
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
            public void Send(string text) { }
            public bool TryDequeueMessage(out AgentMessage message)
            {
                message = Messages.Count == 0 ? null : Messages.Dequeue();
                return message != null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            foreach (var host in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
                Object.DestroyImmediate(host.gameObject);
            background = Application.runInBackground;
            timeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            if (hostObject != null) Object.DestroyImmediate(hostObject);
            VirtualInput.ReleaseAllVirtualInput();
            Application.runInBackground = background;
            Time.timeScale = timeScale;
        }

        [UnityTest]
        public IEnumerator HiddenInputMovesWithoutUiAndShowReusesResources()
        {
            var host = CreateHost();
            yield return null;
            var cursor = host.GetComponent<CursorController>();
            var keyboard = host.GetComponent<KeyboardStatusController>();
            Assert.That(host.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(cursor.enabled, Is.False);
            Assert.That(keyboard.enabled, Is.False);
            Assert.That(AffordanceBootstrap.Following, Is.False);

            var reported = new List<Vector2>();
            yield return cursor.MoveTo(new Vector2(50, 60), reported.Add, true);
            Assert.That(reported.Count, Is.GreaterThan(1));
            Assert.That(reported[reported.Count - 1], Is.EqualTo(new Vector2(50, 60)));
            Assert.That(host.GetComponentsInChildren<Canvas>(true), Is.Empty);

            host.SetOverlay(true);
            var canvases = host.GetComponentsInChildren<Canvas>(true);
            Assert.That(canvases.Length, Is.EqualTo(2));
            var visual = host.transform.Find("Unity Play MCP Virtual Cursor Canvas/Unity Play MCP Virtual Cursor");
            Assert.That((Vector2)visual.position, Is.EqualTo(new Vector2(50, 60)));
            host.SetOverlay(false);
            yield return cursor.MoveTo(new Vector2(80, 90), reported.Add);
            Assert.That(host.GetComponentsInChildren<Canvas>(), Is.Empty);
            Assert.That(cursor.enabled || keyboard.enabled, Is.False);
            host.SetOverlay(true);
            Assert.That(host.GetComponentsInChildren<Canvas>(true), Is.EquivalentTo(canvases));
            Assert.That((Vector2)visual.position, Is.EqualTo(new Vector2(80, 90)));
            AssemblyReloadSimulation.Rehearse(host);
            yield return null;
            Assert.That(host.GetComponentsInChildren<Canvas>(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator FinalDisconnectAndBetweenFrameReplacementEndTheSession()
        {
            var host = CreateHost();
            yield return null;
            var transport = ReplaceTransport(host);
            transport.ActiveClients = 2;
            transport.ClientsOpened = 2;
            yield return null;
            Assert.That(AffordanceBootstrap.Following, Is.False);
            host.StartReadings();
            host.SetOverlay(true);
            var firstRecorder = Field("frameTimeRecorder").GetValue(host);
            transport.ActiveClients = 1;
            yield return null;
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.True);

            // 마지막 client 종료와 재연결이 한 프레임 사이에 일어났다.
            transport.IdleGeneration++;
            transport.ClientsOpened++;
            yield return null;
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);
            Assert.That(AffordanceBootstrap.Following, Is.False);
            Assert.That((bool)Field("collectingPerformance").GetValue(host), Is.False);

            host.StartReadings();
            Assert.That(Field("frameTimeRecorder").GetValue(host), Is.Not.SameAs(firstRecorder));
            transport.ActiveClients = 0;
            transport.IdleGeneration++;
            yield return null;
            Assert.That(host.Reading, Is.False);
            Assert.That(AffordanceBootstrap.Following, Is.False);
            Assert.That((bool)Field("clientSessionActive").GetValue(host), Is.False);
        }

        [UnityTest]
        public IEnumerator DisconnectCancelsTheRestOfABatchBeforeReleasingInput()
        {
            var host = CreateHost();
            yield return null;
            var transport = ReplaceTransport(host);
            transport.ActiveClients = 1;
            transport.ClientsOpened = 1;
            yield return null;
            host.SmoothCursorMovement = true;
            transport.Messages.Enqueue(new AgentMessage(
                "{\"type\":\"ACTION\",\"id\":1,\"actions\":[" +
                "{\"id\":1,\"method\":\"pause_time\",\"params\":[]}," +
                "{\"id\":2,\"method\":\"mouse_down\",\"params\":[0]}," +
                "{\"id\":3,\"method\":\"move_mouse\",\"params\":[300,200]}," +
                "{\"id\":4,\"method\":\"key_down\",\"params\":[\"A\"]}," +
                "{\"id\":5,\"method\":\"set_overlay\",\"params\":[true]}]}", _ => { }, "test"));
            yield return null;
            transport.ActiveClients = 0;
            transport.IdleGeneration++;
            yield return null;
            transport.ActiveClients = 1;
            transport.ClientsOpened++;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(VirtualInput.GetKey(KeyCode.A), Is.False);
            Assert.That(VirtualInput.IsMouseButtonHeld(0), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator FifteenSecondsSuspendsWithAConnectedClientAndPausedTime()
        {
            var host = CreateHost();
            host.SmoothCursorMovement = false;
            yield return null;
            var transport = ReplaceTransport(host);
            transport.ActiveClients = 1;
            transport.ClientsOpened = 1;
            yield return null;
            host.StartReadings();
            host.SetOverlay(true);
            Time.timeScale = 0;
            var last = (double)Field("lastRequestTime").GetValue(host);
            host.SuspendIfIdle(last + 14.999d);
            Assert.That(host.Reading, Is.True);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.True);
            host.SuspendIfIdle(last + 15d);
            Assert.That(host.Reading || AffordanceBootstrap.Following, Is.False);
            Assert.That((bool)Field("collectingPerformance").GetValue(host), Is.False);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);
            yield return null;
            Assert.That(host.TransportOpen, Is.True);
            Assert.That(host.Reading || AffordanceBootstrap.Following, Is.False);

            Send(transport, "runtime_activity", "\"status\"");
            yield return null;
            yield return null;
            Assert.That((bool)Field("collectingPerformance").GetValue(host), Is.True);
            Assert.That(host.Reading || AffordanceBootstrap.Following, Is.False);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);

            Send(transport, "runtime_activity", "\"readings\"");
            yield return null;
            yield return null;
            Assert.That(host.Reading && AffordanceBootstrap.Following, Is.True);
            var run = AffordanceBootstrap.CurrentRun;
            Send(transport, "runtime_activity", "\"status\"");
            yield return null;
            yield return null;
            Assert.That(AffordanceBootstrap.CurrentRun, Is.EqualTo(run));
            host.SuspendIfIdle((double)Field("lastRequestTime").GetValue(host) + 15d);
            Send(transport, "move_mouse", "50,60");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(host.Reading || AffordanceBootstrap.Following, Is.False);
            Assert.That((bool)Field("collectingPerformance").GetValue(host), Is.False);
            Send(transport, "set_overlay", "true");
            yield return null;
            yield return null;
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.True);
            Assert.That(host.Reading, Is.False);
        }

        [UnityTest]
        public IEnumerator BusyActionsDeferIdleAndEmptyMessagesDoNotRenewIt()
        {
            var host = CreateHost();
            yield return null;
            var transport = ReplaceTransport(host);
            transport.ActiveClients = 1;
            yield return null;
            host.SetOverlay(true);
            Field("lastRequestTime").SetValue(host, Time.realtimeSinceStartupAsDouble - 20d);
            Send(transport, "wait_frames", "10");
            yield return null;
            Assert.That((bool)Field("processingActions").GetValue(host), Is.True);
            host.SuspendIfIdle(Time.realtimeSinceStartupAsDouble + 20d);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.True);
            for (var i = 0; i < 15; i++) yield return null;
            var last = (double)Field("lastRequestTime").GetValue(host);
            host.SuspendIfIdle(last + 14.999d);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.True);
            transport.Messages.Enqueue(new AgentMessage("{\"type\":\"ACTION\",\"actions\":[]}", _ => { }, "test"));
            yield return null;
            yield return null;
            Assert.That((double)Field("lastRequestTime").GetValue(host), Is.EqualTo(last));
            foreach (var invalid in new[] { "{", "{\"type\":\"PING\"}",
                "{\"type\":\"ACTION\",\"actions\":[null]}", "{\"type\":\"ACTION\",\"actions\":null}" })
                transport.Messages.Enqueue(new AgentMessage(invalid, _ => { }, "test"));
            yield return null;
            Assert.That((double)Field("lastRequestTime").GetValue(host), Is.EqualTo(last));
            host.SuspendIfIdle(last + 15d);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator OperationLeaseProtectsCollectionUntilItFinishes()
        {
            var host = CreateHost();
            yield return null;
            var transport = ReplaceTransport(host);
            transport.ActiveClients = 1;
            yield return null;
            host.StartReadings();
            host.SetOverlay(true);
            Send(transport, "play_begin", "\"idle-op\",\"hash\",30000");
            yield return null;
            yield return null;
            Assert.That((bool)Field("processingActions").GetValue(host), Is.False);
            host.SuspendIfIdle(Time.realtimeSinceStartupAsDouble + 20d);
            Assert.That(host.Reading, Is.True);
            Send(transport, "play_end", "\"idle-op\"");
            yield return null;
            yield return null;
            host.SuspendIfIdle((double)Field("lastRequestTime").GetValue(host) + 15d);
            Assert.That(host.Reading, Is.False);
            Assert.That(host.GetComponent<KeyboardStatusController>().enabled, Is.False);
        }

        private static void Send(Transport transport, string method, string parameters)
        {
            transport.Messages.Enqueue(new AgentMessage(
                "{\"type\":\"ACTION\",\"id\":1,\"actions\":[{\"id\":1,\"method\":\"" + method +
                "\",\"params\":[" + parameters + "]}]}", _ => { }, "test"));
        }
        private UnityPlayMcpHost CreateHost()
        {
            hostObject = new GameObject("on demand runtime");
            return hostObject.AddComponent<UnityPlayMcpHost>();
        }

        private static FieldInfo Field(string name) => typeof(UnityPlayMcpHost).GetField(name, Private);

        private static Transport ReplaceTransport(UnityPlayMcpHost host)
        {
            var field = Field("webSocketTransport");
            ((IAgentTransport)field.GetValue(host)).Dispose();
            var replacement = new Transport();
            field.SetValue(host, replacement);
            return replacement;
        }
    }
}
