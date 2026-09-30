using System;
using UnityEngine;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// <c>FrameTimingManager</c> 호출을 분리해 평균과 병목 분류를 에디터 없이 테스트할 수 있게 한다.
    /// </summary>
    internal interface IFrameTimingReader
    {
        /// <summary>
        /// 완료된 프레임의 타이밍을 Unity 이력에 쌓는다. 매 프레임 불러야 <see cref="ReadLatest"/> 가 이력을 채워 돌려준다.
        /// </summary>
        void Capture();

        /// <param name="frameCount">요청할 최근 프레임 수. <paramref name="destination"/> 길이를 넘지 않는다.</param>
        /// <returns>
        /// 실제로 채운 개수. Frame Timing Stats 가 꺼져 있으면 예외 없이 0 이다.
        /// </returns>
        int ReadLatest(int frameCount, FrameTimingSample[] destination);
    }

    /// <summary>
    /// 프레임 타이밍을 한 구간의 평균과 병목 분류로 접는다.
    ///
    /// 집계 주기는 <see cref="TrySummarize"/> 를 부르는 쪽이 정한다. 여기에 타이머를 두면 전송 주기와 어긋난다.
    /// <see cref="Record"/> 는 캡처만 하고, 읽기와 평균은 보낼 때 한 번 한다.
    ///
    /// Unity 프레임 타이밍은 몇 프레임 뒤에 확정되므로 <c>FrameTimeRecorder</c> 의 창보다 조금 과거를 가리킨다.
    /// </summary>
    internal sealed class FrameTimingSampler
    {
        /// <summary>
        /// 60fps 1초 구간보다 크다. 실제로 평균한 수는 <see cref="FrameTimingBreakdown.FrameCount"/> 가 알려 준다.
        /// </summary>
        private const int DefaultCapacity = 128;

        /// <summary>
        /// 1등이 2등을 이 배수보다 앞서지 못하면 <see cref="FrameTimingBottleneck.Balanced"/> 로 둔다.
        /// </summary>
        private const float DominanceRatio = 1.1f;

        private readonly IFrameTimingReader reader;
        private readonly FrameTimingSample[] samples;

        /// <summary>직전 집계 이후 캡처한 프레임 수. 버퍼 크기에서 멈춘다.</summary>
        private int capturedFrames;

        public FrameTimingSampler()
            : this(new UnityFrameTimingReader(), DefaultCapacity)
        {
        }

        public FrameTimingSampler(IFrameTimingReader reader, int capacity = DefaultCapacity)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), "Sample capacity must be greater than zero.");
            }

            this.reader = reader;
            samples = new FrameTimingSample[capacity];
        }

        /// <summary>
        /// 매 프레임 부른다. 프레임당 비용을 네이티브 캡처 한 번으로 줄이려고 값은 읽지 않는다.
        /// </summary>
        public void Record()
        {
            reader.Capture();

            // 버퍼 크기에서 멈춘다. 그보다 많이 요청할 일이 없다.
            if (capturedFrames < samples.Length)
            {
                capturedFrames++;
            }
        }

        /// <summary>
        /// 직전 집계 이후 캡처한 프레임만큼 읽어 평균으로 접는다. 두 구간이 같은 프레임을 겹쳐 세지 않는다.
        /// </summary>
        /// <returns>
        /// 읽은 프레임이 없으면 false 다. Frame Timing Stats 가 꺼진 프로젝트에서는 정상이다.
        /// </returns>
        public bool TrySummarize(out FrameTimingBreakdown breakdown)
        {
            breakdown = default;

            var requestedFrames = capturedFrames;
            capturedFrames = 0;

            if (requestedFrames <= 0)
            {
                return false;
            }

            var readFrames = reader.ReadLatest(requestedFrames, samples);
            if (readFrames <= 0)
            {
                return false;
            }

            if (readFrames > samples.Length)
            {
                readFrames = samples.Length;
            }

            var cpu = default(PositiveMean);
            var mainThread = default(PositiveMean);
            var renderThread = default(PositiveMean);
            var gpu = default(PositiveMean);

            for (var i = 0; i < readFrames; i++)
            {
                var sample = samples[i];
                cpu.Add(sample.CpuMs);
                mainThread.Add(sample.CpuMainThreadMs);
                renderThread.Add(sample.CpuRenderThreadMs);
                gpu.Add(sample.GpuMs);
            }

            var cpuMs = cpu.Resolve();
            var mainThreadMs = mainThread.Resolve();
            var renderThreadMs = renderThread.Resolve();
            var gpuMs = gpu.Resolve();

            // 네 항목이 모두 없으면 보고에서 뺀다.
            if (!cpuMs.HasValue && !mainThreadMs.HasValue && !renderThreadMs.HasValue && !gpuMs.HasValue)
            {
                return false;
            }

            breakdown = new FrameTimingBreakdown(
                readFrames,
                cpuMs,
                mainThreadMs,
                renderThreadMs,
                gpuMs,
                Classify(mainThreadMs, renderThreadMs, gpuMs));
            return true;
        }

        /// <summary>
        /// 메인 스레드·렌더 스레드·GPU 중 가장 긴 쪽을 고른다. 없는 항목은 0 으로 두어 후보에서 뺀다.
        /// </summary>
        private static FrameTimingBottleneck Classify(float? mainThreadMs, float? renderThreadMs, float? gpuMs)
        {
            var mainThread = mainThreadMs ?? 0f;
            var renderThread = renderThreadMs ?? 0f;
            var gpu = gpuMs ?? 0f;

            var highest = FrameTimingBottleneck.MainThread;
            var highestMs = mainThread;

            if (renderThread > highestMs)
            {
                highest = FrameTimingBottleneck.RenderThread;
                highestMs = renderThread;
            }

            if (gpu > highestMs)
            {
                highest = FrameTimingBottleneck.Gpu;
                highestMs = gpu;
            }

            if (highestMs <= 0f)
            {
                return FrameTimingBottleneck.Unknown;
            }

            var runnerUpMs = 0f;
            if (highest != FrameTimingBottleneck.MainThread && mainThread > runnerUpMs)
            {
                runnerUpMs = mainThread;
            }

            if (highest != FrameTimingBottleneck.RenderThread && renderThread > runnerUpMs)
            {
                runnerUpMs = renderThread;
            }

            if (highest != FrameTimingBottleneck.Gpu && gpu > runnerUpMs)
            {
                runnerUpMs = gpu;
            }

            // 2등이 없으면 그대로 1등이다.
            if (runnerUpMs > 0f && highestMs <= runnerUpMs * DominanceRatio)
            {
                return FrameTimingBottleneck.Balanced;
            }

            return highest;
        }

        /// <summary>
        /// 양수 값만 모으는 평균. 0 이하는 미수집이라 분모에서도 뺀다.
        /// </summary>
        private struct PositiveMean
        {
            private double total;
            private int count;

            public void Add(double milliseconds)
            {
                if (milliseconds <= 0d)
                {
                    return;
                }

                total += milliseconds;
                count++;
            }

            public float? Resolve()
            {
                return count > 0 ? (float?)(total / count) : null;
            }
        }

        /// <summary>
        /// 실제 <c>FrameTimingManager</c> 를 읽는다.
        ///
        /// Frame Timing Stats 는 프로젝트 설정이라 SDK 가 켤 수 없다. 꺼져 있으면 0 개를 돌려주므로 결과 개수로만 판단한다.
        /// </summary>
        private sealed class UnityFrameTimingReader : IFrameTimingReader
        {
            private FrameTiming[] timings;

            public void Capture()
            {
                FrameTimingManager.CaptureFrameTimings();
            }

            public int ReadLatest(int frameCount, FrameTimingSample[] destination)
            {
                if (frameCount > destination.Length)
                {
                    frameCount = destination.Length;
                }

                // 요청이 늘어날 때만 다시 할당한다.
                if (timings == null || timings.Length < frameCount)
                {
                    timings = new FrameTiming[frameCount];
                }

                var readFrames = (int)FrameTimingManager.GetLatestTimings((uint)frameCount, timings);
                if (readFrames > frameCount)
                {
                    readFrames = frameCount;
                }

                for (var i = 0; i < readFrames; i++)
                {
                    var timing = timings[i];

                    // Unity 가 이미 밀리초로 준다.
                    destination[i] = new FrameTimingSample(
                        timing.cpuFrameTime,
                        timing.cpuMainThreadFrameTime,
                        timing.cpuRenderThreadFrameTime,
                        timing.gpuFrameTime);
                }

                return readFrames;
            }
        }
    }
}
