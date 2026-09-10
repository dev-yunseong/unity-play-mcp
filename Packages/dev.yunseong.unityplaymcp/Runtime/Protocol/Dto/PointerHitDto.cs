using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 포인터가 한쪽 끝에서 무엇을 겨눴고 무엇을 맞혔는지.
    /// </summary>
    /// <remarks>
    /// <c>pointer_click</c> 의 <c>returnValue</c> 이자, <see cref="PointerDragResultDto"/> 의 두
    /// 끝 각각이다.
    /// </remarks>
    internal sealed class PointerHitDto
    {
        /// <summary>호출자가 청한 id.</summary>
        [JsonProperty("targetId")]
        public int TargetId { get; set; }

        /// <summary>
        /// raycast 가 실제로 답한 오브젝트의 id.
        /// </summary>
        /// <remarks>
        /// <c>targetId</c> 와 다를 수 있고, 다른 것이 정상이다. <c>Button</c> 의 graphic 이 자식에
        /// 앉아 있으면 자식이 답하고, 대상이 라벨이면 그 위를 덮은 부모의 <c>Image</c> 가 답한다.
        /// 둘 다 같은 handler 사슬에 닿으므로 성공이지만, 어느 쪽이 답했는지는 호출자가 알아야
        /// 한다.
        /// </remarks>
        [JsonProperty("hitId")]
        public int HitId { get; set; }

        /// <summary>맞은 오브젝트의 이름.</summary>
        [JsonProperty("hit")]
        public string Hit { get; set; }

        /// <summary>
        /// 겨눈 화면 좌표. 좌상단에서 잰 픽셀이다.
        /// </summary>
        /// <remarks>
        /// scan 이 보고하는 좌표계이자 <c>move_mouse</c> 가 받는 좌표계다. 그래서 이 값을 그대로
        /// 되돌려 보내면 같은 자리를 다시 겨눈다.
        /// </remarks>
        [JsonProperty("x")]
        public float X { get; set; }

        /// <inheritdoc cref="X"/>
        [JsonProperty("y")]
        public float Y { get; set; }
    }
}
