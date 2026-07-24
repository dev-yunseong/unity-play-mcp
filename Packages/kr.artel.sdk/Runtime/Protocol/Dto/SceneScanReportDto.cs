using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 등록 시 서버에 보고하는 씬 스캔. 런타임은 로드된 씬만 내용을 볼 수 있으므로,
    /// 로드되지 않은 씬은 <see cref="ScenesInBuild"/>의 이름으로만 남는다.
    /// </summary>
    internal sealed class SceneScanReportDto
    {
        [JsonProperty("scenesInBuild")]
        public List<string> ScenesInBuild { get; set; } = new List<string>();

        [JsonProperty("scannedScenes")]
        public List<SceneDto> ScannedScenes { get; set; } = new List<SceneDto>();
    }
}
