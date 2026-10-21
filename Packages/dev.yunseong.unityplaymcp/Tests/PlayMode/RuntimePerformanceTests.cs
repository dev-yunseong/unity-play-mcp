using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    public sealed class RuntimePerformanceTests
    {
        private const int WarmupFrames = 30;
        private const int SampleFrames = 120;
        private GameObject hostObject;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        private sealed class Sample
        {
            public string state;
            public int samples;
            public int discardedTailSamples;
            public double hostUpdateMeanNanoseconds;
            public long hostUpdateP50Nanoseconds;
            public long hostUpdateP95Nanoseconds;
            public double wholeFrameGcMeanBytes;
        }

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public int warmupFrames = WarmupFrames;
            public int requestedFrames = SampleFrames;
            public string gcScope = "Unity GC Allocated In Frame counter, including Editor/test work; not SDK-only or thread-isolated";
            public string gcUnit = "Bytes";
            public string cpuScope = "UnityPlayMcp.Host.Update only; excludes separate UI/reading callbacks and transport threads";
            public List<Sample> states = new List<Sample>();
        }

        // socket 자체의 비용은 포함하지 않는다. 연결 상태를 고정해 다른 client의 영향을 배제한다.
        private sealed class Transport : IAgentTransport
        {
            public bool IsConnected => true;
            public int ClientsOpened { get; set; }
            public int ActiveClients { get; set; }
            public int IdleGeneration => 0;
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
            public void Send(string text) { }
            public bool TryDequeueMessage(out AgentMessage message) { message = null; return false; }
        }

        [UnityTest, Explicit("Opt-in benchmark; select RuntimePerformanceTests explicitly"), Category("RuntimeBenchmark")]
        public IEnumerator ReportsUnconnectedActiveAndIdleCosts()
        {
            File.Delete(Path.Combine(Application.dataPath, "../runtime-performance.json"));
            foreach (var old in UnityEngine.Object.FindObjectsOfType<UnityPlayMcpHost>(true))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            hostObject = new GameObject("runtime benchmark");
            var host = hostObject.AddComponent<UnityPlayMcpHost>();
            var transport = new Transport();
            // Start가 실행되기 전에 넣어 benchmark host가 실제 socket을 열지 않게 한다.
            Field("webSocketTransport").SetValue(host, transport);
            yield return null;
            var report = new Report { unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType };
            yield return CalibrateGcCounter();
            AssertState(host, false);
            yield return Measure("unconnected", report);
            AssertState(host, false);
            transport.ActiveClients = 1;
            transport.ClientsOpened = 1;
            yield return null;
            host.StartReadings();
            host.SetOverlay(true);
            AssertState(host, true);
            yield return Measure("active", report);
            AssertState(host, true);
            host.SuspendIfIdle((double)Field("lastRequestTime").GetValue(host) + 15d);
            AssertState(host, false);
            yield return Measure("idle", report);
            AssertState(host, false);
            Assert.That(host.TransportOpen, Is.True);
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Application.dataPath, "../runtime-performance.json"), json);
            TestContext.Progress.WriteLine(json);
        }

        [TearDown]
        public void TearDown()
        {
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private static IEnumerator Measure(string state, Report report)
        {
            for (var i = 0; i < WarmupFrames; i++) yield return null;
            // 고정된 120프레임 표본을 보존하려고 wraparound를 끈다.
            var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using (var cpu = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "UnityPlayMcp.Host.Update", SampleFrames, options))
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", SampleFrames, options))
            {
                Assert.That(cpu.Valid, Is.True, "Required CPU profiling recorder unavailable");
                Assert.That(cpu.UnitType.ToString(), Is.EqualTo("TimeNanoseconds"));
                Assert.That(gc.Valid, Is.True, "Required GC byte counter unavailable");
                Assert.That(gc.UnitType.ToString(), Is.EqualTo("Bytes"));
                for (var i = 0; i < SampleFrames; i++) yield return null;
                cpu.Stop();
                gc.Stop();
                var cpuSamples = cpu.ToArray();
                var gcSamples = gc.ToArray();
                // Stop 시점의 진행 중 프레임 표본 하나가 붙을 수 있으므로 앞의 완성된 120개만 쓴다.
                Assert.That(cpuSamples.Length, Is.InRange(SampleFrames, SampleFrames + 1), "Incomplete or unexpected CPU window");
                Assert.That(gcSamples.Length, Is.InRange(SampleFrames, SampleFrames + 1), "Incomplete or unexpected GC window");
                Assert.That(gcSamples.Length, Is.EqualTo(cpuSamples.Length), "CPU and GC windows must align");
                var values = new long[SampleFrames];
                double cpuSum = 0, gcSum = 0;
                for (var i = 0; i < values.Length; i++) { values[i] = cpuSamples[i].Value; cpuSum += values[i]; gcSum += gcSamples[i].Value; }
                Array.Sort(values);
                report.states.Add(new Sample { state = state, samples = values.Length,
                    discardedTailSamples = cpuSamples.Length - SampleFrames,
                    hostUpdateMeanNanoseconds = cpuSum / values.Length,
                    hostUpdateP50Nanoseconds = values[(int)Math.Ceiling(values.Length * .50) - 1],
                    hostUpdateP95Nanoseconds = values[(int)Math.Ceiling(values.Length * .95) - 1],
                    wholeFrameGcMeanBytes = gcSum / SampleFrames });
            }
        }

        private static IEnumerator CalibrateGcCounter()
        {
            // 지원되지 않는 byte counter가 0을 반환해 무할당으로 오해되는 것을 막는다.
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 8))
            {
                Assert.That(gc.Valid, Is.True, "GC byte counter unavailable");
                Assert.That(gc.UnitType.ToString(), Is.EqualTo("Bytes"));
                yield return null;
                var allocation = new byte[4096];
                yield return null;
                yield return null;
                GC.KeepAlive(allocation);
                gc.Stop();
                var observedAllocation = false;
                foreach (var sample in gc.ToArray()) observedAllocation |= sample.Value >= 4096;
                Assert.That(observedAllocation, Is.True, "GC byte counter did not observe a known 4096-byte allocation");
            }
        }

        private static FieldInfo Field(string name) => typeof(UnityPlayMcpHost).GetField(name, Private);
        private static void AssertState(UnityPlayMcpHost host, bool active)
        {
            Assert.That(host.Reading, Is.EqualTo(active));
            Assert.That((bool)Field("collectingPerformance").GetValue(host), Is.EqualTo(active));
            Assert.That(host.GetComponentsInChildren<Canvas>().Length, Is.EqualTo(active ? 2 : 0));
        }
    }
}
