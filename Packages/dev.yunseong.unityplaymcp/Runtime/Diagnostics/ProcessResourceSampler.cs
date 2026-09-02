// 프로세스 지표는 데스크톱 개발 빌드와 에디터에서만 읽는다. 모바일·콘솔·WebGL에서는
// System.Diagnostics.Process가 런타임에 PlatformNotSupportedException을 던지거나 0만 돌려주므로,
// 실물 리더를 아예 컴파일에서 잘라 낸다.
#if UNITY_EDITOR || ((UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && DEVELOPMENT_BUILD)
#define UNITY_PLAY_MCP_PROCESS_RESOURCES_SUPPORTED
#endif

using System;
using System.Diagnostics;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 프로세스 CPU·메모리의 원시 reading 값. OS와 GC를 직접 읽는 지점을 여기 하나로 모아,
    /// 델타 계산은 실제 프로세스 없이도 테스트할 수 있게 둔다.
    /// </summary>
    internal interface IProcessResourceReader
    {
        /// <summary>프로세스가 지금까지 쓴 CPU 시간의 누적. 코어 수만큼 실시간보다 빨리 는다.</summary>
        TimeSpan TotalProcessorTime { get; }

        long WorkingSetBytes { get; }
        long PrivateBytes { get; }
        long ManagedHeapBytes { get; }

        int GetCollectionCount(int generation);
    }

    /// <summary>
    /// 프로세스 CPU·메모리를 한 구간의 델타로 접는다.
    ///
    /// **집계 주기를 소유하지 않는다.** 경과 시간과 코어 수를 인자로 받으므로, 창의 길이는
    /// <see cref="TrySample"/>을 부르는 쪽이 정한다. <c>FrameTimeRecorder</c>와 같은 모양이고,
    /// 이유도 같다 — 여기에 타이머를 두면 전송 주기와 두 벌이 되어 서로 어긋난다.
    ///
    /// Unity API를 직접 읽지 않는다. <c>SystemInfo.processorCount</c>도 호출자가 넘긴다.
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
        /// 이 플랫폼에서 읽을 수 있으면 샘플러를, 아니면 null을 돌려준다. 호출자는 null이면
        /// 보고에서 CPU·메모리 항목을 통째로 뺀다. 여기서 0을 채운 샘플러를 대신 주면
        /// "안 재는 플랫폼"과 "정말 놀고 있는 프로세스"가 구분되지 않는다.
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
        /// 논리 코어 수. 호출자는 <c>SystemInfo.processorCount</c>를 넘긴다. CPU 시간은 코어마다
        /// 따로 쌓이므로, 나누지 않으면 8코어에서 800%까지 나온다.
        /// </param>
        /// <returns>
        /// 첫 호출은 항상 false. 누적값의 차이가 곧 사용량이라 비교 대상이 없는 구간은 계산 자체가
        /// 성립하지 않는다. 0을 보내면 소비자가 놀고 있는 프로세스로 읽는다.
        /// </returns>
        public bool TrySample(float elapsedSeconds, int processorCount, out ProcessResourceUsage usage)
        {
            usage = default;

            // 분모가 없으면 비율도 없다. 기준점은 건드리지 않는다 — 여기서 갱신하면 이미 지나간
            // CPU 시간이 어느 구간에도 안 실리고 사라진다.
            if (elapsedSeconds <= 0f)
            {
                return false;
            }

            var processorTime = reader.TotalProcessorTime;

            // 런타임의 GC.MaxGeneration이 무엇이든 보고 스키마는 0·1·2로 고정한다. 세대 수를 따라
            // 필드가 늘거나 줄면 서버 쪽 해석이 깨진다.
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
            // processorCount가 0인 플랫폼이 있다. 그대로 나누면 무한대가 나가므로 한 코어로 본다.
            var cores = processorCount > 0 ? processorCount : 1;

            var used = processorTimeDelta.TotalSeconds / (elapsedSeconds * (double)cores) * PercentScale;

            // 경과 시간은 호출자의 벽시계이고 CPU 시간은 OS의 회계라, 둘의 어긋남이 값을 100 밖으로
            // 조금 밀어낼 수 있다. 비율로 정의한 필드이므로 범위를 지킨다.
            if (used < 0d)
            {
                return 0f;
            }

            return used > PercentScale ? PercentScale : (float)used;
        }

#if UNITY_PLAY_MCP_PROCESS_RESOURCES_SUPPORTED
        /// <summary>
        /// 실제 OS·GC reading. <see cref="ProcessResourceSampler"/>가 이 타입을 직접 알지 않도록
        /// 인터페이스 뒤에 둔다.
        /// </summary>
        private sealed class CurrentProcessResourceReader : IProcessResourceReader
        {
            /// <summary>
            /// 매번 <see cref="Process.GetCurrentProcess"/>를 부르지 않는다. 호출마다 OS 핸들을 쥔
            /// 객체가 새로 생기고, Dispose하지 않으면 매 초 핸들이 샌다. 자기 프로세스라 대상이
            /// 바뀔 일도 없다.
            /// </summary>
            private readonly Process process = Process.GetCurrentProcess();

            /// <summary>
            /// 메모리 값은 Process가 스냅샷으로 캐시한다. <see cref="Process.Refresh"/>는 그 캐시를
            /// 버릴 뿐이라 비용이 없고, 이걸 빼면 프로세스가 살아 있는 내내 첫 reading 값이 반복된다.
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

            /// <summary>수집을 강제하지 않는다. 지표를 읽는 쪽이 프레임을 멈추게 할 수는 없다.</summary>
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
