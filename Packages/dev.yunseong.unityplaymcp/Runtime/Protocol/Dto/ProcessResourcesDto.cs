using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 프로세스 CPU·메모리 사용량.
    /// </summary>
    /// <remarks>
    /// 프로세스 전체 값이라 에디터에서는 에디터 비용이 포함되어 빌드 값과 비교하면 안 된다.
    ///
    /// 직전 보고 이후의 델타다. 첫 구간과 지원하지 않는 플랫폼(모바일·콘솔·WebGL)에서는 블록째 빠진다.
    ///
    /// 메모리 단위는 반올림하지 않은 바이트다.
    /// </remarks>
    public sealed class ProcessResourcesDto
    {
        /// <summary>
        /// 코어 수로 나눈 0~100 비율. 8코어에서 코어 하나를 다 쓰면 12.5 다.
        /// </summary>
        [JsonProperty("cpuPercent")]
        public float CpuPercent { get; set; }

        /// <summary>
        /// 실제로 올라와 있는 물리 메모리(RSS). OS 회수에 따라 줄 수 있어 감소가 해제를 뜻하지 않는다.
        /// </summary>
        [JsonProperty("workingSetBytes")]
        public long WorkingSetBytes { get; set; }

        /// <summary>
        /// 공유 매핑을 뺀 커밋 크기. 누수 추적에 적합하다. 플랫폼에 따라 0 일 수 있다.
        /// </summary>
        [JsonProperty("privateBytes")]
        public long PrivateBytes { get; set; }

        /// <summary>
        /// 매니지드 힙 사용량. 회수되지 않은 쓰레기가 섞이고 네이티브 할당은 빠진다.
        /// </summary>
        [JsonProperty("managedHeapBytes")]
        public long ManagedHeapBytes { get; set; }

        /// <summary>이 구간에 일어난 0세대 수집 횟수.</summary>
        [JsonProperty("gen0Collections")]
        public int Gen0Collections { get; set; }

        [JsonProperty("gen1Collections")]
        public int Gen1Collections { get; set; }

        [JsonProperty("gen2Collections")]
        public int Gen2Collections { get; set; }

        /// <summary>
        /// 이 델타가 덮은 구간의 길이. 보고가 빠지면 전송 주기보다 길어진다.
        /// </summary>
        [JsonProperty("sampledMs")]
        public float SampledMs { get; set; }
    }
}
