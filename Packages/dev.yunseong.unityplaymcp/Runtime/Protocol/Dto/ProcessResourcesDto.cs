using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 한 전송 구간의 프로세스 CPU·메모리 사용량.
    /// </summary>
    /// <remarks>
    /// OS가 프로세스 전체에 매긴 값이라 게임 코드만의 비용이 아니다. 에디터에서 재면 에디터
    /// 프로세스가 통째로 들어가므로, 빌드에서 잰 값과 직접 비교하면 안 된다.
    ///
    /// 누적값이 아니라 직전 보고 이후의 델타다. 첫 구간은 비교 대상이 없어 이 블록 자체가 빠지고,
    /// 프로세스 지표를 읽지 않는 플랫폼(모바일·콘솔·WebGL)에서도 빠진다. 0으로 채워 보내면
    /// "안 쟀다"와 "놀고 있다"가 구분되지 않는다.
    ///
    /// 메모리 단위는 바이트다. MB로 미리 접으면 서버가 반올림 이전 값을 되돌릴 수 없다.
    /// </remarks>
    public sealed class ProcessResourcesDto
    {
        /// <summary>
        /// 0~100. 코어 수로 나눈 뒤라 모든 코어를 태워야 100이고, 코어 하나를 다 쓰면 8코어에서
        /// 12.5다. 코어당 비율로 세는 작업 관리자와는 눈금이 다르다.
        /// </summary>
        [JsonProperty("cpuPercent")]
        public float CpuPercent { get; set; }

        /// <summary>
        /// 실제로 올라와 있는 물리 메모리(RSS). OS의 회수 압력에 따라 오르내리므로 이 값의 감소가
        /// 곧 해제는 아니다.
        /// </summary>
        [JsonProperty("workingSetBytes")]
        public long WorkingSetBytes { get; set; }

        /// <summary>
        /// 공유 매핑을 뺀 커밋 크기. 누수 추적에는 <c>workingSetBytes</c>보다 이쪽이 낫다. 다만
        /// 플랫폼과 런타임에 따라 집계되지 않아 0이 올라올 수 있다.
        /// </summary>
        [JsonProperty("privateBytes")]
        public long PrivateBytes { get; set; }

        /// <summary>
        /// 매니지드 힙 사용량. 수집을 강제하지 않고 읽은 값이라 아직 회수되지 않은 쓰레기가 섞이고,
        /// 텍스처·메시 같은 네이티브 할당은 애초에 빠져 있다. 프로세스 메모리와 차이가 나는 이유다.
        /// </summary>
        [JsonProperty("managedHeapBytes")]
        public long ManagedHeapBytes { get; set; }

        /// <summary>이 구간에 일어난 0세대 수집 횟수. <c>sampledMs</c>와 함께 봐야 빈도가 나온다.</summary>
        [JsonProperty("gen0Collections")]
        public int Gen0Collections { get; set; }

        [JsonProperty("gen1Collections")]
        public int Gen1Collections { get; set; }

        [JsonProperty("gen2Collections")]
        public int Gen2Collections { get; set; }

        /// <summary>
        /// 이 델타가 덮은 구간의 길이. 수집 횟수와 CPU 비율은 창 길이를 모르면 해석할 수 없고,
        /// 보고가 한 번 빠지면 창이 전송 주기의 두 배가 된다.
        /// </summary>
        [JsonProperty("sampledMs")]
        public float SampledMs { get; set; }
    }
}
