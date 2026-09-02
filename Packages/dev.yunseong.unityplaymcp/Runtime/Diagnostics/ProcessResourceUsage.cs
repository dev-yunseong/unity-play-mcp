namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 한 집계 구간의 프로세스 CPU·메모리 사용량.
    ///
    /// OS가 프로세스 전체에 매긴 값이라 게임 코드만의 비용이 아니다. 에디터에서 재면 에디터
    /// 프로세스가 통째로 들어간다.
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
        /// 코어 수로 나눈 0~100 비율. 8코어를 전부 태워도 100이며, 코어 하나를 다 쓰면
        /// 8코어에서 12.5다. 작업 관리자처럼 코어당으로 읽으면 안 된다.
        /// </summary>
        public float CpuPercent { get; }

        /// <summary>실제로 올라와 있는 물리 메모리(RSS). OS의 회수 압력에 따라 흔들린다.</summary>
        public long WorkingSetBytes { get; }

        /// <summary>공유 매핑을 뺀 커밋 크기. 누수 추적에는 이쪽이 낫다.</summary>
        public long PrivateBytes { get; }

        /// <summary>
        /// 매니지드 힙 사용량. 수집을 강제하지 않고 읽으므로 아직 회수되지 않은 쓰레기가 섞이고,
        /// 텍스처 같은 네이티브 할당은 애초에 빠져 있다.
        /// </summary>
        public long ManagedHeapBytes { get; }

        /// <summary>이 구간에 일어난 0세대 수집 횟수. 누적값이 아니라 델타다.</summary>
        public int Gen0Collections { get; }

        public int Gen1Collections { get; }
        public int Gen2Collections { get; }

        /// <summary>
        /// 이 델타가 덮은 구간의 길이. 수집 횟수와 CPU 비율은 창 길이를 모르면 해석할 수 없다.
        /// </summary>
        public float SampledSeconds { get; }
    }
}
