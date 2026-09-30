using System;
using System.Collections.Generic;
using UnityPlayMcp.Diagnostics;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Diagnostics
{
    public sealed class FrameTimingSamplerTests
    {
        /// <summary>
        /// test 가 프레임 타이밍을 정하는 reader 다. FrameTimingManager 와 GPU 를 쓰지 않는다.
        /// </summary>
        private sealed class FakeFrameTimingReader : IFrameTimingReader
        {
            private readonly Queue<FrameTimingSample> pending = new Queue<FrameTimingSample>();

            public int CaptureCount { get; private set; }

            /// <summary>마지막 <see cref="ReadLatest"/> 가 요청받은 프레임 수.</summary>
            public int LastRequestedFrames { get; private set; }

            public void Capture()
            {
                CaptureCount++;
            }

            public void Enqueue(double cpuMs, double mainThreadMs, double renderThreadMs, double gpuMs)
            {
                pending.Enqueue(new FrameTimingSample(cpuMs, mainThreadMs, renderThreadMs, gpuMs));
            }

            public int ReadLatest(int frameCount, FrameTimingSample[] destination)
            {
                LastRequestedFrames = frameCount;

                var readFrames = 0;
                while (readFrames < frameCount && pending.Count > 0)
                {
                    destination[readFrames++] = pending.Dequeue();
                }

                return readFrames;
            }
        }

        /// <summary>큐에 넣은 프레임 수만큼 실제 호출 순서대로 캡처한다.</summary>
        private static void RecordFrames(FrameTimingSampler sampler, int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                sampler.Record();
            }
        }

        [Test]
        public void Constructor_RejectsANullReader()
        {
            Assert.Throws<ArgumentNullException>(() => new FrameTimingSampler(null));
        }

        [Test]
        public void Constructor_RejectsANonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FrameTimingSampler(new FakeFrameTimingReader(), 0));
        }

        [Test]
        public void Record_OnlyCapturesAndDefersReadingToTheSummary()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);

            RecordFrames(sampler, 3);

            // 매 프레임 도는 곳이므로 읽기와 평균은 여기서 하면 안 된다.
            Assert.AreEqual(3, reader.CaptureCount);
            Assert.AreEqual(0, reader.LastRequestedFrames);
        }

        [Test]
        public void TrySummarize_ReturnsFalseWithoutAnyRecordedFrame()
        {
            var sampler = new FrameTimingSampler(new FakeFrameTimingReader());

            Assert.IsFalse(sampler.TrySummarize(out _));
        }

        [Test]
        public void TrySummarize_ReturnsFalseWhenFrameTimingStatsAreOff()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            RecordFrames(sampler, 60);

            // 설정이 꺼진 project 는 0 개를 돌려준다. 0 으로 채워 보내면 CPU 와 GPU 비용이 없는 프레임으로 읽힌다.
            Assert.IsFalse(sampler.TrySummarize(out _));
        }

        [Test]
        public void TrySummarize_AveragesEachTimingOverTheWindow()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 9d, renderThreadMs: 4d, gpuMs: 6d);
            reader.Enqueue(cpuMs: 20d, mainThreadMs: 11d, renderThreadMs: 6d, gpuMs: 10d);
            RecordFrames(sampler, 2);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(2, breakdown.FrameCount);
            Assert.AreEqual(15f, breakdown.CpuMs.Value, 1e-3f);
            Assert.AreEqual(10f, breakdown.CpuMainThreadMs.Value, 1e-3f);
            Assert.AreEqual(5f, breakdown.CpuRenderThreadMs.Value, 1e-3f);
            Assert.AreEqual(8f, breakdown.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void TrySummarize_KeepsMillisecondsAsRead()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 16.7d, mainThreadMs: 16.7d, renderThreadMs: 8.3d, gpuMs: 12.5d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            // Unity 가 이미 ms 로 준다. 1000 을 곱하면 안 된다.
            Assert.AreEqual(16.7f, breakdown.CpuMs.Value, 1e-3f);
            Assert.AreEqual(12.5f, breakdown.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void TrySummarize_LeavesOutTimingsTheDriverDidNotReport()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);

            // GPU timer 가 없는 드라이버와 render thread 가 없는 구성은 0 으로 온다.
            reader.Enqueue(cpuMs: 12d, mainThreadMs: 12d, renderThreadMs: 0d, gpuMs: 0d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.IsFalse(breakdown.GpuMs.HasValue);
            Assert.IsFalse(breakdown.CpuRenderThreadMs.HasValue);
            Assert.IsTrue(breakdown.CpuMs.HasValue);
        }

        [Test]
        public void TrySummarize_ExcludesUnreportedFramesFromTheMeanInsteadOfCountingThemAsZero()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 0d);
            RecordFrames(sampler, 2);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            // 0 을 뺀, 실제로 잰 프레임의 평균이어야 한다.
            Assert.AreEqual(8f, breakdown.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void TrySummarize_ReturnsFalseWhenNoTimingCameBackPositive()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 0d, mainThreadMs: 0d, renderThreadMs: 0d, gpuMs: 0d);
            RecordFrames(sampler, 1);

            // 프레임 수만 남은 그룹은 보내지 않는다.
            Assert.IsFalse(sampler.TrySummarize(out _));
        }

        [Test]
        public void TrySummarize_AsksOnlyForFramesRecordedSinceTheLastSummary()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            RecordFrames(sampler, 2);

            Assert.IsTrue(sampler.TrySummarize(out _));
            Assert.AreEqual(2, reader.LastRequestedFrames);

            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            RecordFrames(sampler, 1);

            // 더 요청하면 이전 구간에 실은 프레임을 다시 센다.
            Assert.IsTrue(sampler.TrySummarize(out _));
            Assert.AreEqual(1, reader.LastRequestedFrames);
        }

        [Test]
        public void TrySummarize_NeverAsksForMoreFramesThanTheBufferHolds()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader, capacity: 4);
            for (var i = 0; i < 10; i++)
            {
                reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            }

            RecordFrames(sampler, 10);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(4, reader.LastRequestedFrames);
            Assert.AreEqual(4, breakdown.FrameCount);
        }

        [Test]
        public void TrySummarize_ClassifiesTheGpuAsTheBottleneckWhenItIsTheLongestPole()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 12d, mainThreadMs: 12d, renderThreadMs: 6d, gpuMs: 30d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(FrameTimingBottleneck.Gpu, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_ClassifiesTheMainThreadAsTheBottleneckWhenItIsTheLongestPole()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 30d, mainThreadMs: 30d, renderThreadMs: 6d, gpuMs: 8d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(FrameTimingBottleneck.MainThread, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_ClassifiesTheRenderThreadAsTheBottleneckWhenItIsTheLongestPole()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 30d, mainThreadMs: 9d, renderThreadMs: 28d, gpuMs: 8d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(FrameTimingBottleneck.RenderThread, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_RefusesToPickABottleneckAmongCloseTimings()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 16d, mainThreadMs: 16d, renderThreadMs: 8d, gpuMs: 15.9d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            // 0.1ms 차이로 병목을 정하면 보고마다 결과가 뒤집힌다.
            Assert.AreEqual(FrameTimingBottleneck.Balanced, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_ClassifiesAmongTheTimingsItActuallyRead()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);

            // GPU 타이밍이 없으면 남은 두 값으로 판정한다.
            reader.Enqueue(cpuMs: 30d, mainThreadMs: 29d, renderThreadMs: 5d, gpuMs: 0d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out var breakdown));

            Assert.AreEqual(FrameTimingBottleneck.MainThread, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_StartsAFreshWindowAfterEachSummary()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 10d, mainThreadMs: 10d, renderThreadMs: 5d, gpuMs: 8d);
            RecordFrames(sampler, 1);

            Assert.IsTrue(sampler.TrySummarize(out _));

            // 새 프레임을 캡처하기 전에는 읽을 것이 없다.
            Assert.IsFalse(sampler.TrySummarize(out _));
        }
    }
}
