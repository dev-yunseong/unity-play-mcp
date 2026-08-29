using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 등록 시 보고하는 씬 트리 노드. 스트리밍용 <see cref="SceneBlockDto"/>와 달리
    /// GameObject 인스턴스 ID를 담지 않는다. 인스턴스 ID는 실행마다 바뀌는 런타임 값이라
    /// 빌드에 영구 저장하면 무의미하다.
    /// </summary>
    internal sealed class SceneScanBlockDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("components")]
        public List<SceneComponentDto> Components { get; set; } = new List<SceneComponentDto>();

        [JsonProperty("children")]
        public List<SceneScanBlockDto> Children { get; set; } = new List<SceneScanBlockDto>();
    }
}
