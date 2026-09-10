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
        /// 여기 실린 <c>hitId</c> 는 <b>누르기 전에</b> 목적지 좌표에서 확인한 오브젝트이지, 놓는
        /// 순간 포인터 아래 있던 것이 아니다. 드래그되는 오브젝트가 포인터를 따라오는 게임이
        /// 흔하고, 그때 놓는 순간 포인터 아래 있는 것은 목적지가 아니라 끌려온 그 오브젝트다.
        /// 그것을 보고하면 정상 드래그마다 목적지를 놓친 것처럼 읽힌다.
        /// </remarks>
        [JsonProperty("to")]
        public PointerHitDto To { get; set; }
    }
}
