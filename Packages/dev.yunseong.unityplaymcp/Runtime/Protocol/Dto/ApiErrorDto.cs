using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class ApiErrorDto
    {
        [JsonProperty("code")]
        public string Code { get; set; }
    }
}
