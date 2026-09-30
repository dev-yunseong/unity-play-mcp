using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    public sealed class ActionResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        /// <summary>
        /// The <c>id</c> of the ACTION this answers.
        /// </summary>
        /// <remarks>
        /// Separate from <c>id</c>, which is this message's own outgoing number;
        /// the two counters are unrelated.
        /// </remarks>
        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public long? RequestId { get; set; }

        /// <summary>
        /// 배치의 마지막 액션이 끝난 프레임.
        /// </summary>
        /// <remarks>
        /// 받는 쪽은 이 값을 pulse 의 <c>frame</c> 과 비교해 액션 이후의 pulse 를 가린다. 시간으로 어림하면
        /// 액션 전에 잡힌 pulse 를 결과로 읽는다.
        ///
        /// pulse 와 같은 <see cref="UnityEngine.Time.frameCount"/> 다. 커서 이동처럼 여러 프레임에 걸치는
        /// 액션이 있으므로 큐에 넣은 프레임이 아니라 배치가 <b>끝난 프레임</b>이다. 배치 전체에 하나다.
        /// </remarks>
        [JsonProperty("frame")]
        public int Frame { get; set; }

        [JsonProperty("results")]
        public List<ActionResultDto> Results { get; set; } = new List<ActionResultDto>();
    }
}
