using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 보고 시점의 런타임 상태.
    /// </summary>
    /// <remarks>
    /// <see cref="DeviceContextDto"/>와 달리 세션 중에 바뀌는 값이라 보고마다 다시 싣는다.
    /// 세션에 한 번만 실으면 "언제부터 바뀌었는지"를 복원할 수 없다.
    /// </remarks>
    public sealed class RuntimeStatusDto
    {
        /// <summary>
        /// 이 보고 시점의 포커스 상태. Unity는 포커스를 잃은 창의 프레임 페이싱을 스로틀링하므로,
        /// 이 값이 없으면 백그라운드로 눌린 구간을 성능 저하로 오진한다.
        /// </summary>
        [JsonProperty("isFocused")]
        public bool IsFocused { get; set; }

        /// <summary>
        /// 배터리 상태 이름(<c>Charging</c>, <c>Discharging</c>, <c>NotCharging</c>,
        /// <c>Full</c>, <c>Unknown</c>).
        ///
        /// 노트북·모바일이 배터리로 돌면 전원 정책이 클럭을 낮추거나 발열로 스로틀링이 걸린다.
        /// 그 구간의 프레임 저하는 코드 회귀가 아니므로, 분석에서 분리할 수 있도록 함께 싣는다.
        /// </summary>
        [JsonProperty("batteryStatus")]
        public string BatteryStatus { get; set; }
    }
}
