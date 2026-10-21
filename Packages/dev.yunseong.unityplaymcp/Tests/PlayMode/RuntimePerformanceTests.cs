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
            public double hostUpdateMeanNanoseconds;
            public long hostUpdateP50Nanoseconds;
            public long hostUpdateP95Nanoseconds;
            public double mainThreadGcMeanBytes;
        }

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public int warmupFrames = WarmupFrames;
            public int requestedFrames = SampleFrames;
            public string gcScope = "Entire main thread including Editor/test overhead, not SDK-only";
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
            foreach (var old in UnityEngine.Object.FindObjectsOfType<UnityPlayMcpHost>(true))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            hostObject = new GameObject("runtime benchmark");
            var host = hostObject.AddComponent<UnityPlayMcpHost>();
            yield return null;
            ((IAgentTransport)Field("webSocketTransport").GetValue(host)).Dispose();
            var transport = new Transport();
            Field("webSocketTransport").SetValue(host, transport);
            var report = new Report { unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType };
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
            var options = ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using (var cpu = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "UnityPlayMcp.Host.Update", SampleFrames, options))
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", SampleFrames, options))
            {
                Assert.That(cpu.Valid && gc.Valid, Is.True, "Required profiling recorders unavailable");
                for (var i = 0; i < SampleFrames; i++) yield return null;
                cpu.Stop();
                gc.Stop();
                var cpuSamples = cpu.ToArray();
                var gcSamples = gc.ToArray();
                Assert.That(cpuSamples.Length, Is.GreaterThan(0), "No CPU samples; do not report missing data as zero");
                Assert.That(gcSamples.Length, Is.GreaterThan(0), "No GC samples; do not report missing data as zero");
                var values = new long[cpuSamples.Length];
                double cpuSum = 0, gcSum = 0;
                for (var i = 0; i < values.Length; i++) { values[i] = cpuSamples[i].Value; cpuSum += values[i]; }
                foreach (var sample in gcSamples) gcSum += sample.Value;
                Array.Sort(values);
                report.states.Add(new Sample { state = state, samples = values.Length,
                    hostUpdateMeanNanoseconds = cpuSum / values.Length,
                    hostUpdateP50Nanoseconds = values[(int)Math.Ceiling(values.Length * .50) - 1],
                    hostUpdateP95Nanoseconds = values[(int)Math.Ceiling(values.Length * .95) - 1],
                    mainThreadGcMeanBytes = gcSum / gcSamples.Length });
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
