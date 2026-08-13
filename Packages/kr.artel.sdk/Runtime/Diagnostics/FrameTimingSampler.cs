using System;
using UnityEngine;

namespace Artel.Diagnostics
{
    /// <summary>
    /// 프레임 타이밍의 원시 판독. Unity의 <c>FrameTimingManager</c>를 부르는 지점을 여기 하나로
    /// 모아, 평균과 병목 분류는 에디터 없이도 시험할 수 있게 둔다.
    /// </summary>
    internal interface IFrameTimingReader
    {
        /// <summary>
        /// 완료된 프레임의 타이밍을 Unity 쪽 이력으로 끌어온다. 매 프레임 부르지 않으면 이력이
        /// 채워지지 않아 <see cref="ReadLatest"/>가 몇 프레임만 돌려준다.
        /// </summary>
        void Capture();

        /// <param name="frameCount">요청할 최근 프레임 수. <paramref name="destination"/> 길이를 넘지 않는다.</param>
        /// <returns>
        /// 실제로 채운 개수. Frame Timing Stats가 꺼져 있으면 0이다 — 예외가 아니라 미수집이다.
        /// </returns>
        int ReadLatest(int frameCount, FrameTimingSample[] destination);
    }

    /// <summary>
    /// 프레임 타이밍을 한 구간의 평균과 병목 분류로 접는다.
    ///
    /// **집계 주기를 소유하지 않는다.** 창의 길이는 <see cref="TrySummarize"/>를 부르는 쪽이
    /// 정한다. <c>FrameTimeRecorder</c>·<c>ProcessResourceSampler</c>와 같은 모양이고, 이유도
    /// 같다 — 여기에 타이머를 두면 전송 주기와 두 벌이 되어 서로 어긋난다.
    ///
    /// 매 프레임 도는 <see cref="Record"/>는 Unity에 캡처만 시키고 값을 읽지 않는다. 읽기와
    /// 평균은 전송 게이트가 열릴 때 한 번만 돈다. 샘플 버퍼는 생성자에서 한 번 잡고 재사용한다.
    ///
    /// 이 값들은 <c>FrameTimeRecorder</c>가 재는 창과 같은 창이 아니다. Unity의 프레임 타이밍은
    /// 몇 프레임 뒤에 확정되므로 이력은 항상 조금 과거를 가리킨다.
    /// </summary>
    internal sealed class FrameTimingSampler
    {
        /// <summary>
        /// 60fps 1초 구간을 덮고도 남는 크기. Unity의 이력이 더 짧으면 더 짧은 만큼만 돌아오고,
        /// 실제로 몇 프레임을 평균했는지는 <see cref="FrameTimingBreakdown.FrameCount"/>가 알려 준다.
        /// </summary>
        private const int DefaultCapacity = 128;

        /// <summary>
        /// 1등이 병목으로 불리기 위해 2등을 앞서야 하는 배수. 차이가 이보다 작으면
        /// <see cref="FrameTimingBottleneck.Balanced"/>로 둔다.
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
        /// 매 프레임 부른다. 여기서 값을 읽지 않는 것이 요점이다 — 프레임당 비용을 네이티브 캡처
        /// 한 번으로 묶어 둔다.
        /// </summary>
        public void Record()
        {
            reader.Capture();

            // 버퍼보다 많이 요청할 일이 없으므로 여기서 멈춰 둔다. 전송이 오래 끊겨 있어도
            // 카운터가 무한정 자라지 않는다.
            if (capturedFrames < samples.Length)
            {
                capturedFrames++;
            }
        }

        /// <summary>
        /// 직전 집계 이후 캡처한 프레임만큼 읽어 평균으로 접는다. 캡처한 수를 넘겨 요청하지
        /// 않으므로, 두 구간이 같은 프레임을 겹쳐 세지 않는다.
        /// </summary>
        /// <returns>
        /// 읽어 온 프레임이 없으면 false. Frame Timing Stats가 꺼진 프로젝트의 정상 경로이고,
        /// 이때 0으로 채운 결과를 내보내면 소비자가 "CPU도 GPU도 공짜인 프레임"으로 읽는다.
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

            // 네 항목이 전부 비었다면 프레임 수만 남은 껍데기가 된다. 보고에서 빼는 편이 낫다.
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
        /// 메인 스레드·렌더 스레드·GPU 중 가장 긴 쪽을 고른다. 없는 항목은 0으로 두어 후보에서
        /// 저절로 빠진다.
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

            // 비교할 상대가 하나뿐이면 그대로 1등이다. 상대가 있으면 배수만큼 앞서야 한다.
            if (runnerUpMs > 0f && highestMs <= runnerUpMs * DominanceRatio)
            {
                return FrameTimingBottleneck.Balanced;
            }

            return highest;
        }

        /// <summary>
        /// 양수 판독만 모으는 평균. 0이나 음수는 "그 항목을 못 쟀다"는 뜻이라 분모에서도 빼야
        /// 나머지 프레임의 평균이 눌리지 않는다.
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
        /// 실제 <c>FrameTimingManager</c> 판독. <see cref="FrameTimingSampler"/>가 Unity API를
        /// 직접 알지 않도록 인터페이스 뒤에 둔다.
        ///
        /// Frame Timing Stats는 프로젝트 설정이라 SDK가 켤 수 없다. 꺼져 있으면 예외 없이 0개를
        /// 돌려주므로, 별도의 지원 여부 질의 없이 결과 개수로만 판단한다.
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

                // 요청이 늘어날 때만 다시 잡는다. 집계는 초당 한 번이지만 매번 할당할 이유는 없다.
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

                    // Unity가 이미 밀리초로 준다. 여기서 환산하면 두 배가 된다.
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
