using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 세션의 하드웨어·렌더링·빌드 컨텍스트. 연결마다 한 번 올린다.
    /// </summary>
    /// <remarks>
    /// <c>PERFORMANCE</c>에 실어 매초 보내지 않는 이유는 20개 필드가 세션 내내 바뀌지 않기
    /// 때문이다. 그렇다고 등록 시점에만 보내면 재연결한 서버 인스턴스가 컨텍스트를 모른 채
    /// 성능 보고만 받게 되므로, 연결 단위로 한 번씩 다시 보낸다.
    /// </remarks>
    public sealed class DeviceContextMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("device")]
        public DeviceContextDto Device { get; set; }
    }
}
