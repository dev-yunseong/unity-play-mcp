using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// <c>pointer_drag</c> 의 <c>returnValue</c>. 드래그의 두 끝.
    /// </summary>
    internal sealed class PointerDragResultDto
    {
        /// <summary>버튼을 누른 자리.</summary>
        [JsonProperty("from")]
        public PointerHitDto From { get; set; }

        /// <summary>
        /// 버튼을 놓은 자리.
        /// </summary>
        /// <remarks>
        /// <c>hitId</c> 는 <b>누르기 전에</b> 목적지 좌표에서 확인한 오브젝트다. 놓는 순간에는
        /// 끌려온 오브젝트가 포인터 아래 있기 쉬워 목적지를 놓친 것처럼 읽힌다.
        /// </remarks>
        [JsonProperty("to")]
        public PointerHitDto To { get; set; }
    }
}
