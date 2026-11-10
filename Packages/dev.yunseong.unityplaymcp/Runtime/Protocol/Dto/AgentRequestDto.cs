using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace UnityPlayMcp.Protocol.Dto
{
    // JSON 역직렬화가 기본 생성자와 프로퍼티 setter를 리플렉션으로 호출한다.
    [Preserve]
    internal sealed class AgentRequestDto
    {
        [Preserve, JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// The sender's id for this request, echoed back on the ACTION_RESULT.
        /// </summary>
        [Preserve, JsonProperty("id")]
        public long Id { get; set; }

        [Preserve, JsonProperty("method")]
        public string Method { get; set; }

        [Preserve, JsonProperty("actions")]
        public List<ActionRequestDto> Actions { get; set; } = new List<ActionRequestDto>();
    }
}
