using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    internal sealed class ActionRequestDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public List<object> Parameters { get; set; } = new List<object>();
    }
}
