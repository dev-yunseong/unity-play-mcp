namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 한 집계 구간의 프로세스 CPU·메모리 사용량. 프로세스 전체 값이라 에디터에서는 에디터 비용이 포함된다.
    /// </summary>
    internal readonly struct ProcessResourceUsage
    {
        public ProcessResourceUsage(
            float cpuPercent,
            long workingSetBytes,
            long privateBytes,
            long managedHeapBytes,
            int gen0Collections,
            int gen1Collections,
            int gen2Collections,
            float sampledSeconds)
        {
            CpuPercent = cpuPercent;
            WorkingSetBytes = workingSetBytes;
            PrivateBytes = privateBytes;
            ManagedHeapBytes = managedHeapBytes;
            Gen0Collections = gen0Collections;
            Gen1Collections = gen1Collections;
            Gen2Collections = gen2Collections;
            SampledSeconds = sampledSeconds;
        }

        /// <summary>
        /// 코어 수로 나눈 0~100 비율. 8코어에서 코어 하나를 다 쓰면 12.5 다.
        /// </summary>
        public float CpuPercent { get; }

        /// <summary>실제로 올라와 있는 물리 메모리(RSS). OS의 회수 압력에 따라 흔들린다.</summary>
        public long WorkingSetBytes { get; }

        /// <summary>공유 매핑을 뺀 커밋 크기. 누수 추적에는 이쪽이 낫다.</summary>
        public long PrivateBytes { get; }

        /// <summary>
        /// 매니지드 힙 사용량. 회수되지 않은 쓰레기가 섞이고 네이티브 할당은 빠진다.
        /// </summary>
        public long ManagedHeapBytes { get; }

        /// <summary>이 구간에 일어난 0세대 수집 횟수. 누적값이 아니라 델타다.</summary>
        public int Gen0Collections { get; }

        public int Gen1Collections { get; }
        public int Gen2Collections { get; }

        /// <summary>이 델타가 덮은 구간의 길이.</summary>
        public float SampledSeconds { get; }
    }
}
