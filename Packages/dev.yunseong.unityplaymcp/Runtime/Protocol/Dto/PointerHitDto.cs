using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 포인터가 겨눈 대상과 실제로 맞힌 대상.
    /// </summary>
    /// <remarks>
    /// <c>pointer_click</c>·<c>pointer_hover</c> 의 <c>returnValue</c> 이고 <see cref="PointerDragResultDto"/> 의 각 끝이다.
    /// hover 에서는 포인터가 도착한 뒤 다시 raycast 한 결과다.
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
        /// <c>targetId</c> 와 다를 수 있다. <c>Button</c> 의 graphic 이 자식에 있거나 라벨을 부모의
        /// <c>Image</c> 가 덮으면 그쪽이 맞는다. 같은 handler 에 닿으므로 성공이다.
        /// </remarks>
        [JsonProperty("hitId")]
        public int HitId { get; set; }

        /// <summary>맞은 오브젝트의 이름.</summary>
        [JsonProperty("hit")]
        public string Hit { get; set; }

        /// <summary>
        /// 겨눈 화면 좌표. 좌상단 기준 픽셀이다.
        /// </summary>
        /// <remarks>
        /// scan 과 <c>move_mouse</c> 가 쓰는 좌표계라 그대로 다시 보낼 수 있다.
        /// </remarks>
        [JsonProperty("x")]
        public float X { get; set; }

        /// <inheritdoc cref="X"/>
        [JsonProperty("y")]
        public float Y { get; set; }
    }
}
