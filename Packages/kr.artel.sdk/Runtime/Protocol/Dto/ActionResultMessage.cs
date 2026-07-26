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

        [JsonProperty("results")]
        public List<ActionResultDto> Results { get; set; } = new List<ActionResultDto>();
    }
}
