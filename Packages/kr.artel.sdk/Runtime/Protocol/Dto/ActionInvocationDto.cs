using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ActionInvocationDto
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("returnValue")]
        public object ReturnValue { get; set; }

        [JsonProperty("timeStamp")]
        public string Timestamp { get; set; }
    }
}
