using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace UnityPlayMcp.Protocol.Dto
{
    // JSON 역직렬화가 기본 생성자와 프로퍼티 setter를 리플렉션으로 호출한다.
    [Preserve]
    internal sealed class ActionRequestDto
    {
        [Preserve, JsonProperty("id")]
        public int Id { get; set; }

        [Preserve, JsonProperty("method")]
        public string Method { get; set; }

        [Preserve, JsonProperty("params")]
        public List<object> Parameters { get; set; } = new List<object>();
    }
}
