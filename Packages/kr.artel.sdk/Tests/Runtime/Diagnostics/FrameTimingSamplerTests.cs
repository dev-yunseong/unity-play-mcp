using System;
using System.Collections.Generic;
using Artel.Diagnostics;
using NUnit.Framework;

namespace Artel.Tests.Diagnostics
{
    public sealed class FrameTimingSamplerTests
    {
        /// <summary>
        /// 프레임 타이밍을 시험이 직접 정하는 리더. Unity의 FrameTimingManager도, 실제 GPU도
        /// 건드리지 않는다.
        /// </summary>
        private sealed class FakeFrameTimingReader : IFrameTimingReader
        {
            private readonly Queue<FrameTimingSample> pending = new Queue<FrameTimingSample>();

            public int CaptureCount { get; private set; }

            /// <summary>직전 <see cref="ReadLatest"/>가 요청받은 프레임 수.</summary>
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

        /// <summary>큐에 넣은 프레임 수만큼 캡처를 돌린다. 실제 호출 순서와 같게 둔다.</summary>
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

            // 매 프레임 도는 자리라 읽기와 평균은 여기서 일어나면 안 된다.
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

            // 설정이 꺼진 프로젝트는 예외 없이 0개를 돌려준다. 0으로 채운 결과를 내보내면
            // 소비자가 CPU도 GPU도 공짜인 프레임으로 읽는다.
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

            // Unity가 이미 ms로 준다. 초로 보고 1000을 곱하면 값이 천 배가 된다.
            Assert.AreEqual(16.7f, breakdown.CpuMs.Value, 1e-3f);
            Assert.AreEqual(12.5f, breakdown.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void TrySummarize_LeavesOutTimingsTheDriverDidNotReport()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);

            // GPU 타이머를 주지 않는 드라이버와 렌더 스레드가 없는 구성. 둘 다 0으로 온다.
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

            // 8과 0의 평균인 4가 아니라, 실제로 잰 프레임의 평균인 8이어야 한다.
            Assert.AreEqual(8f, breakdown.GpuMs.Value, 1e-3f);
        }

        [Test]
        public void TrySummarize_ReturnsFalseWhenNoTimingCameBackPositive()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);
            reader.Enqueue(cpuMs: 0d, mainThreadMs: 0d, renderThreadMs: 0d, gpuMs: 0d);
            RecordFrames(sampler, 1);

            // 프레임 수만 남은 껍데기를 보내느니 그룹을 통째로 뺀다.
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

            // 더 요청하면 이전 구간에 이미 실은 프레임을 다시 세게 된다.
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

            // 0.1ms 차이로 병목을 단정하면 보고마다 이름이 뒤집힌다.
            Assert.AreEqual(FrameTimingBottleneck.Balanced, breakdown.Bottleneck);
        }

        [Test]
        public void TrySummarize_ClassifiesAmongTheTimingsItActuallyRead()
        {
            var reader = new FakeFrameTimingReader();
            var sampler = new FrameTimingSampler(reader);

            // GPU 타이밍이 없는 환경. 남은 두 값만으로 가른다.
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
