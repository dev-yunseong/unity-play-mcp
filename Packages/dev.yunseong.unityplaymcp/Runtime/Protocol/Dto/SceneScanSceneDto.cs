using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 등록 시 보고하는 씬 하나. 씬 핸들(id) 없이 이름과 트리만 담는다.
    /// </summary>
    internal sealed class SceneScanSceneDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("children")]
        public List<SceneScanBlockDto> Children { get; set; } = new List<SceneScanBlockDto>();
    }
}
