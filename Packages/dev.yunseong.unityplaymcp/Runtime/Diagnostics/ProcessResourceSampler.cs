// 프로세스 지표는 데스크톱 개발 빌드와 에디터에서만 읽는다. 다른 플랫폼에서는
// System.Diagnostics.Process 가 PlatformNotSupportedException 을 던지거나 0 만 돌려준다.
#if UNITY_EDITOR || ((UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && DEVELOPMENT_BUILD)
#define UNITY_PLAY_MCP_PROCESS_RESOURCES_SUPPORTED
#endif

using System;
using System.Diagnostics;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 프로세스 CPU·메모리의 원시 값. OS 와 GC 를 읽는 지점을 분리해 델타 계산을 테스트할 수 있게 한다.
    /// </summary>
    internal interface IProcessResourceReader
    {
        /// <summary>프로세스가 쓴 CPU 시간의 누적. 코어마다 따로 쌓인다.</summary>
        TimeSpan TotalProcessorTime { get; }

        long WorkingSetBytes { get; }
        long PrivateBytes { get; }
        long ManagedHeapBytes { get; }

        int GetCollectionCount(int generation);
    }

    /// <summary>
    /// 프로세스 CPU·메모리를 한 구간의 델타로 접는다.
    ///
    /// 집계 주기는 <see cref="TrySample"/> 를 부르는 쪽이 정한다. 여기에 타이머를 두면
    /// 전송 주기와 어긋난다. Unity API 를 직접 읽지 않는다.
    /// </summary>
    internal sealed class ProcessResourceSampler
    {
        private const float PercentScale = 100f;

        private readonly IProcessResourceReader reader;

        private TimeSpan previousProcessorTime;
        private int previousGen0Collections;
        private int previousGen1Collections;
        private int previousGen2Collections;
        private bool hasPreviousReading;

        public ProcessResourceSampler(IProcessResourceReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            this.reader = reader;
        }

        /// <summary>
        /// 지원하지 않는 플랫폼이면 null 을 돌려준다. 이때 호출자는 CPU·메모리 항목을 뺀다.
        /// 0 을 채우면 놀고 있는 프로세스와 구분되지 않는다.
        /// </summary>
        public static ProcessResourceSampler CreateForCurrentPlatform()
        {
#if UNITY_PLAY_MCP_PROCESS_RESOURCES_SUPPORTED
            return new ProcessResourceSampler(new CurrentProcessResourceReader());
#else
            return null;
#endif
        }

        /// <param name="elapsedSeconds">직전 성공한 샘플 이후의 실제 경과 시간. CPU 비율의 분모다.</param>
        /// <param name="processorCount">
        /// <c>SystemInfo.processorCount</c>. CPU 시간을 코어 수로 나눠 100% 를 넘지 않게 한다.
        /// </param>
        /// <returns>
        /// 첫 호출은 기준점만 잡고 false 를 돌려준다.
        /// </returns>
        public bool TrySample(float elapsedSeconds, int processorCount, out ProcessResourceUsage usage)
        {
            usage = default;

            // 기준점은 갱신하지 않는다. 갱신하면 지나간 CPU 시간이 어느 구간에도 실리지 않는다.
            if (elapsedSeconds <= 0f)
            {
                return false;
            }

            var processorTime = reader.TotalProcessorTime;

            // GC.MaxGeneration 과 무관하게 보고 스키마는 0·1·2 로 고정한다. 필드가 바뀌면 서버 해석이 깨진다.
            var gen0Collections = reader.GetCollectionCount(0);
            var gen1Collections = reader.GetCollectionCount(1);
            var gen2Collections = reader.GetCollectionCount(2);

            if (!hasPreviousReading)
            {
                StoreBaseline(processorTime, gen0Collections, gen1Collections, gen2Collections);
                hasPreviousReading = true;
                return false;
            }

            usage = new ProcessResourceUsage(
                CpuPercent(processorTime - previousProcessorTime, elapsedSeconds, processorCount),
                reader.WorkingSetBytes,
                reader.PrivateBytes,
                reader.ManagedHeapBytes,
                gen0Collections - previousGen0Collections,
                gen1Collections - previousGen1Collections,
                gen2Collections - previousGen2Collections,
                elapsedSeconds);

            StoreBaseline(processorTime, gen0Collections, gen1Collections, gen2Collections);
            return true;
        }

        private void StoreBaseline(
            TimeSpan processorTime,
            int gen0Collections,
            int gen1Collections,
            int gen2Collections)
        {
            previousProcessorTime = processorTime;
            previousGen0Collections = gen0Collections;
            previousGen1Collections = gen1Collections;
            previousGen2Collections = gen2Collections;
        }

        private static float CpuPercent(TimeSpan processorTimeDelta, float elapsedSeconds, int processorCount)
        {
            // processorCount 가 0 인 플랫폼이 있어 한 코어로 본다.
            var cores = processorCount > 0 ? processorCount : 1;

            var used = processorTimeDelta.TotalSeconds / (elapsedSeconds * (double)cores) * PercentScale;

            // 벽시계 경과 시간과 OS 의 CPU 시간이 어긋나 0~100 밖으로 나갈 수 있어 자른다.
            if (used < 0d)
            {
                return 0f;
            }

            return used > PercentScale ? PercentScale : (float)used;
        }

#if UNITY_PLAY_MCP_PROCESS_RESOURCES_SUPPORTED
        /// <summary>실제 OS·GC 값을 읽는다.</summary>
        private sealed class CurrentProcessResourceReader : IProcessResourceReader
        {
            /// <summary>
            /// 한 번만 얻는다. <see cref="Process.GetCurrentProcess"/> 는 호출마다 OS 핸들을 쥔 객체를
            /// 만들어 Dispose 하지 않으면 핸들이 샌다.
            /// </summary>
            private readonly Process process = Process.GetCurrentProcess();

            /// <summary>
            /// Process 는 값을 캐시하므로 <see cref="Process.Refresh"/> 없이는 첫 값이 반복된다.
            /// </summary>
            public TimeSpan TotalProcessorTime
            {
                get
                {
                    process.Refresh();
                    return process.TotalProcessorTime;
                }
            }

            public long WorkingSetBytes
            {
                get
                {
                    process.Refresh();
                    return process.WorkingSet64;
                }
            }

            public long PrivateBytes
            {
                get
                {
                    process.Refresh();
                    return process.PrivateMemorySize64;
                }
            }

            /// <summary>수집을 강제하지 않는다. 강제하면 프레임이 멈춘다.</summary>
            public long ManagedHeapBytes
            {
                get { return GC.GetTotalMemory(false); }
            }

            public int GetCollectionCount(int generation)
            {
                return GC.CollectionCount(generation);
            }
        }
#endif
    }
}
