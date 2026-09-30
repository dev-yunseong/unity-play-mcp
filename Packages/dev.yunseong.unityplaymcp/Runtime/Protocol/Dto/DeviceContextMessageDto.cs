using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 세션의 하드웨어·렌더링·빌드 컨텍스트. 연결마다 한 번 올린다.
    /// </summary>
    /// <remarks>
    /// 세션 내내 바뀌지 않아 <c>PERFORMANCE</c> 에 싣지 않는다. 재연결한 서버 인스턴스도 받도록
    /// 등록 시점이 아니라 연결마다 보낸다.
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
