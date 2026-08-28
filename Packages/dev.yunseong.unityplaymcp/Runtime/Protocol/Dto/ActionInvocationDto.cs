using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ActionInvocationDto
    {
        [JsonProperty("sequence")]
        public long Sequence { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("returnValue")]
        public object ReturnValue { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public ActionErrorDto Error { get; set; }

        [JsonProperty("timeStamp")]
        public string Timestamp { get; set; }
    }
}
