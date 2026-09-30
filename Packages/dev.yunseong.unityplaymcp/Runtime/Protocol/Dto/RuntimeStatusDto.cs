using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 보고 시점의 런타임 상태.
    /// </summary>
    /// <remarks>
    /// 세션 중에 바뀌는 값이라 <see cref="DeviceContextDto"/> 와 달리 보고마다 싣는다.
    /// </remarks>
    public sealed class RuntimeStatusDto
    {
        /// <summary>
        /// 보고 시점의 포커스 상태. Unity 는 포커스를 잃은 창을 스로틀링하므로 성능 저하와 구분하는 데 쓴다.
        /// </summary>
        [JsonProperty("isFocused")]
        public bool IsFocused { get; set; }

        /// <summary>
        /// 배터리 상태 이름(<c>Charging</c>, <c>Discharging</c>, <c>NotCharging</c>,
        /// <c>Full</c>, <c>Unknown</c>). 배터리 구동 시 스로틀링을 코드 회귀와 구분하는 데 쓴다.
        /// </summary>
        [JsonProperty("batteryStatus")]
        public string BatteryStatus { get; set; }
    }
}
