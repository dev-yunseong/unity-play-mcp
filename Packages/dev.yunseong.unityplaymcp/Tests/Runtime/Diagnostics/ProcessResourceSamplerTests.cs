using System;
using UnityPlayMcp.Diagnostics;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Diagnostics
{
    public sealed class ProcessResourceSamplerTests
    {
        private const float WindowSeconds = 1f;

        /// <summary>test 가 값을 정하는 reader 다. 실제 프로세스와 GC 를 쓰지 않는다.</summary>
        private sealed class FakeProcessResourceReader : IProcessResourceReader
        {
            private readonly int[] collectionCounts = new int[3];

            public TimeSpan TotalProcessorTime { get; set; }
            public long WorkingSetBytes { get; set; }
            public long PrivateBytes { get; set; }
            public long ManagedHeapBytes { get; set; }

            public int GetCollectionCount(int generation)
            {
                return collectionCounts[generation];
            }

            public void UseProcessorSeconds(double seconds)
            {
                TotalProcessorTime += TimeSpan.FromSeconds(seconds);
            }

            /// <summary>GC 카운터는 누적값이므로 test 도 누적으로 올려야 델타 계산이 검증된다.</summary>
            public void Collect(int generation, int times)
            {
                collectionCounts[generation] += times;
            }
        }

        /// <summary>
        /// 첫 <c>TrySample</c> 은 기준점만 잡고 false 를 돌려주므로 여기서 한 번 호출해 둔다.
        /// </summary>
        private static ProcessResourceSampler Started(FakeProcessResourceReader reader)
        {
            var sampler = new ProcessResourceSampler(reader);
            Assert.IsFalse(sampler.TrySample(WindowSeconds, 1, out _));
            return sampler;
        }

        [Test]
        public void Constructor_RejectsANullReader()
        {
            Assert.Throws<ArgumentNullException>(() => new ProcessResourceSampler(null));
        }

        [Test]
        public void TrySample_ReturnsFalseOnTheFirstSample()
        {
            var sampler = new ProcessResourceSampler(new FakeProcessResourceReader());

            // 이전 값이 없다. 0 을 보내면 유휴 프로세스로 읽힌다.
            Assert.IsFalse(sampler.TrySample(WindowSeconds, 4, out _));
        }

        [Test]
        public void TrySample_DerivesCpuPercentFromTheProcessorTimeDelta()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);
            reader.UseProcessorSeconds(0.5d);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 1, out var usage));

            Assert.AreEqual(50f, usage.CpuPercent, 1e-3f);
        }

        [Test]
        public void TrySample_NormalizesCpuPercentByProcessorCount()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);

            // 코어 하나를 1초 동안 쓴 경우다. 4코어에서는 25% 다.
            reader.UseProcessorSeconds(1d);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 4, out var usage));

            Assert.AreEqual(25f, usage.CpuPercent, 1e-3f);
        }

        [Test]
        public void TrySample_TreatsAnUnknownProcessorCountAsOneCore()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);
            reader.UseProcessorSeconds(0.25d);

            // processorCount 가 0 인 플랫폼이 있다. 그대로 나누면 무한대가 된다.
            Assert.IsTrue(sampler.TrySample(WindowSeconds, 0, out var usage));

            Assert.AreEqual(25f, usage.CpuPercent, 1e-3f);
        }

        [Test]
        public void TrySample_KeepsCpuPercentWithinTheReportedRange()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);

            // 벽시계와 OS 회계가 어긋나면 용량을 넘을 수 있다. 비율 필드이므로 잘라야 한다.
            reader.UseProcessorSeconds(3d);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 2, out var usage));

            Assert.AreEqual(100f, usage.CpuPercent, 1e-3f);
        }

        [Test]
        public void TrySample_ReportsZeroCpuForAnIdleWindow()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 4, out var usage));

            Assert.AreEqual(0f, usage.CpuPercent, 1e-6f);
        }

        [Test]
        public void TrySample_PassesMemoryReadingsThroughAsAbsoluteBytes()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);
            reader.WorkingSetBytes = 512L * 1024L * 1024L;
            reader.PrivateBytes = 640L * 1024L * 1024L;
            reader.ManagedHeapBytes = 48L * 1024L * 1024L;

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 4, out var usage));

            // 메모리는 델타가 아니라 그 시점의 값이다.
            Assert.AreEqual(512L * 1024L * 1024L, usage.WorkingSetBytes);
            Assert.AreEqual(640L * 1024L * 1024L, usage.PrivateBytes);
            Assert.AreEqual(48L * 1024L * 1024L, usage.ManagedHeapBytes);
        }

        [Test]
        public void TrySample_ReportsCollectionCountsAsWindowDeltas()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);
            reader.Collect(0, 12);
            reader.Collect(1, 3);
            reader.Collect(2, 1);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 4, out var first));

            Assert.AreEqual(12, first.Gen0Collections);
            Assert.AreEqual(3, first.Gen1Collections);
            Assert.AreEqual(1, first.Gen2Collections);

            reader.Collect(0, 5);

            Assert.IsTrue(sampler.TrySample(WindowSeconds, 4, out var second));

            // 누적값이 아니라 이 구간의 증가분이다.
            Assert.AreEqual(5, second.Gen0Collections);
            Assert.AreEqual(0, second.Gen1Collections);
            Assert.AreEqual(0, second.Gen2Collections);
        }

        [Test]
        public void TrySample_ReportsTheWindowItCovered()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);

            Assert.IsTrue(sampler.TrySample(2.5f, 4, out var usage));

            Assert.AreEqual(2.5f, usage.SampledSeconds, 1e-6f);
        }

        [Test]
        public void TrySample_RejectsWindowsWithoutElapsedTime()
        {
            var reader = new FakeProcessResourceReader();
            var sampler = Started(reader);
            reader.UseProcessorSeconds(0.5d);

            Assert.IsFalse(sampler.TrySample(0f, 1, out _));
            Assert.IsFalse(sampler.TrySample(-1f, 1, out _));

            // 거절된 호출은 기준점을 옮기지 않으므로 그동안의 CPU 시간이 다음 구간에 실린다.
            Assert.IsTrue(sampler.TrySample(WindowSeconds, 1, out var usage));
            Assert.AreEqual(50f, usage.CpuPercent, 1e-3f);
        }
    }
}
