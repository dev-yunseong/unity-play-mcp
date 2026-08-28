using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
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
        /// A separate field rather than reusing <c>id</c>, which is this message's
        /// own outgoing number and is what every existing reader keys on. Without
        /// an echo the server cannot tell which request a result belongs to; it
        /// was matching on <c>id</c> and finding nothing, because the two counters
        /// are unrelated.
        /// </remarks>
        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public long? RequestId { get; set; }

        /// <summary>
        /// 배치의 마지막 액션이 끝난 프레임.
        /// </summary>
        /// <remarks>
        /// 판독은 자기가 언제 잡혔는지를 말하는데(<c>LiveState</c> 가 <c>frame</c> 을 싣는다) 이 답은 말하지
        /// 않았다. 그래서 받는 쪽이 어떤 판독이 이 액션 이후의 것인지 가릴 방법이 없었고, 시간으로 어림잡았다 —
        /// 액션을 보내고 잠시 안에 판독이 하나 더 오면 그것을 결과로 쳤다.
        ///
        /// 그 어림이 틀린다. 읽기와 전달이 두 속도라, 액션 직후 도착하는 첫 배치는 <b>액션 전에 잡힌 것</b>일 수
        /// 있다. 그러면 부르는 쪽은 액션 이전 화면을 액션의 결과로 읽고, 아무 일도 안 일어났다고 판단해 같은 것을
        /// 다시 보낸다. 실제 QA 런에서 그렇게 됐다.
        ///
        /// <see cref="UnityEngine.Time.frameCount"/> 다 — 판독이 쓰는 바로 그 시계. 다른 시계였다면 이 값과
        /// 판독의 값을 견주는 일이 뜻을 잃는다.
        ///
        /// 큐에 넣은 프레임이 아니라 <b>끝난 프레임</b>이다. 커서 활강처럼 여러 프레임에 걸치는 액션이 있고, 그런
        /// 액션에서는 둘이 갈린다. 기다리는 쪽이 궁금한 것은 배치가 끝난 뒤의 화면이다.
        ///
        /// 배치 전체에 하나다. 액션마다 찍어도 부르는 쪽이 쓸 것은 마지막 것뿐이다.
        /// </remarks>
        [JsonProperty("frame")]
        public int Frame { get; set; }

        [JsonProperty("results")]
        public List<ActionResultDto> Results { get; set; } = new List<ActionResultDto>();
    }
}
