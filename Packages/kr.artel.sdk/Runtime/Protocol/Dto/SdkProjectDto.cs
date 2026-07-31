using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class SdkProjectDto
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    internal sealed class SdkProjectsResponseDto
    {
        [JsonProperty("projects")]
        public List<SdkProjectDto> Projects { get; set; }
    }
}
