using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 성공한 <c>scan_evidence</c> 의 <c>returnValue</c>.
    /// </summary>
    /// <remarks>
    /// 무엇을 올렸는지를 적는다. 성공했다는 말만으로는 화면이 방금 앉은 표가 이번 스캔의 것인지 앞선 스캔의 것인지 가릴 수
    /// 없고, 문서 지문이 그것을 가른다.
    /// </remarks>
    internal sealed class EvidenceScanResultDto
    {
        [JsonProperty("objectKey")]
        public string ObjectKey { get; set; }

        [JsonProperty("evidenceDigest")]
        public string EvidenceDigest { get; set; }

        [JsonProperty("byteSize")]
        public long ByteSize { get; set; }

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("sceneCount")]
        public int SceneCount { get; set; }

        /// <summary>등록에 실린 씬 화면 수. 못 찍은 것과 못 올린 것도 사실로 세므로 씬 수보다 작을 수 있다.</summary>
        [JsonProperty("sceneCapturesRegistered")]
        public int SceneCapturesRegistered { get; set; }

        [JsonProperty("alreadyRegistered")]
        public bool AlreadyRegistered { get; set; }
    }
}
