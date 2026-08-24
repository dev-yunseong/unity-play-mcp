using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 근거 문서를 올릴 단기 URL 을 청한다. 바이트는 orchestration 을 지나가지 않는다.
    /// </summary>
    /// <remarks>
    /// 캡처 티켓과 같은 이유로 두 걸음이다: 문서는 실측 1,413 KB 이고 WebFlux 의 기본 요청 버퍼 상한은 256 KB 다. 그 상한을
    /// 올리면 모든 엔드포인트가 함께 올라가므로, 서버는 스토리지에 직접 올릴 서명을 내주는 쪽을 골랐다.
    ///
    /// 필드 이름은 서버의 <c>EvidenceUploadTicketRequest</c> 를 그대로 따른다 — <c>byteSize</c> 가 아니라
    /// <c>contentLength</c> 다.
    /// </remarks>
    internal sealed class EvidenceUploadTicketRequestDto
    {
        [JsonProperty("contentLength")]
        public long ContentLength { get; set; }
    }

    internal sealed class EvidenceUploadTicketResponseDto
    {
        /// <summary>등록 때 다시 보내는 이름. 서버는 이것으로 올라온 문서를 찾아 직접 읽는다.</summary>
        [JsonProperty("objectKey")]
        public string ObjectKey { get; set; }

        [JsonProperty("uploadUrl")]
        public string UploadUrl { get; set; }

        /// <summary>서명이 덮는 헤더들. 이것 없이 보낸 PUT 은 스토리지가 거절한다.</summary>
        [JsonProperty("requiredHeaders")]
        public Dictionary<string, string> RequiredHeaders { get; set; }

        [JsonProperty("uploadExpiresAt")]
        public string UploadExpiresAt { get; set; }
    }

    /// <summary>
    /// 올라간 문서를 빌드에 붙인다. <c>objectKey</c> 하나만 보낸다.
    /// </summary>
    /// <remarks>
    /// schema · capture · build 를 SDK 가 신고하지 않는 것은 서버의 결정이다. 그 값이 문서와 어긋나도 서버는 알 수 없으므로,
    /// 서버가 올라온 문서의 앞부분을 직접 읽는다.
    /// </remarks>
    internal sealed class RegisterEvidenceDocumentRequestDto
    {
        [JsonProperty("objectKey")]
        public string ObjectKey { get; set; }
    }

    internal sealed class RegisterEvidenceDocumentResponseDto
    {
        [JsonProperty("contentMapId")]
        public long ContentMapId { get; set; }

        [JsonProperty("documentId")]
        public long DocumentId { get; set; }

        [JsonProperty("capture")]
        public string Capture { get; set; }

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("evidenceDigest")]
        public string EvidenceDigest { get; set; }

        [JsonProperty("byteSize")]
        public long ByteSize { get; set; }

        /// <summary>
        /// 실패가 아니다. 같은 문서를 다시 올리면 서버가 저장도 적재도 건너뛰고 기존 행을 그대로 돌려준다.
        /// </summary>
        [JsonProperty("alreadyRegistered")]
        public bool AlreadyRegistered { get; set; }
    }
}
