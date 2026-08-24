using System;
using System.Collections;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine.Networking;

namespace Artel.Evidence
{
    /// <summary>
    /// 올라간 근거 문서가 어느 빌드의 무엇이 되었는지, 또는 왜 되지 못했는지.
    /// </summary>
    internal struct EvidenceUpload
    {
        public string ObjectKey;
        public string EvidenceDigest;
        public long ByteSize;
        public int SchemaVersion;

        /// <summary>같은 문서가 이미 등록되어 있었는지. 실패가 아니다.</summary>
        public bool AlreadyRegistered;

        /// <summary>업로드가 성공했으면 null.</summary>
        public string Error;

        public bool IsSuccess { get { return Error == null; } }

        public static EvidenceUpload Failed(string error)
        {
            return new EvidenceUpload { Error = error };
        }
    }

    internal interface IEvidenceUploader
    {
        IEnumerator Upload(byte[] document, Action<EvidenceUpload> completed);
    }

    /// <summary>
    /// 근거 문서를 세 걸음으로 올린다: 티켓을 받고, 스토리지에 PUT 하고, 등록한다.
    /// </summary>
    /// <remarks>
    /// 캡처 업로드와 같은 모양이고 같은 이유다. 문서는 실측 1,413 KB 이고 orchestration 을 통과시키면 그 서버의 요청 버퍼
    /// 상한을 모든 엔드포인트에 대해 올려야 한다. 바이트는 스토리지로 직접 간다.
    ///
    /// 아무것도 재시도하지 않는다. 다시 올릴지는 그 실행을 쥔 쪽이 시나리오를 보고 내리는 판단이지, 시도 횟수를 세는 이
    /// 클라이언트가 내릴 수 있는 판단이 아니다.
    ///
    /// 자격 증명은 만들 때가 아니라 올릴 때 읽는다. 게임이 켜진 뒤에도 로그인과 등록은 한참 뒤에 끝날 수 있고, 그 전에 청해진
    /// 업로드는 낡은 값으로 올라가는 대신 그렇다고 말해야 한다.
    /// </remarks>
    internal sealed class EvidenceUploader : IEvidenceUploader
    {
        private const string ContentMapPathFormat = "/api/sdk/game-builds/{0}/content-map";

        private readonly IJsonCodec jsonCodec;
        private readonly Func<Server> server;
        private readonly Func<string> token;
        private readonly Func<string> gameBuildId;

        public EvidenceUploader(
            IJsonCodec jsonCodec, Func<Server> server, Func<string> token, Func<string> gameBuildId)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.token = token ?? throw new ArgumentNullException(nameof(token));
            this.gameBuildId = gameBuildId ?? throw new ArgumentNullException(nameof(gameBuildId));
        }

        public IEnumerator Upload(byte[] document, Action<EvidenceUpload> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (document == null || document.Length == 0)
            {
                completed(EvidenceUpload.Failed("The evidence document is empty, so there is nothing to upload."));
                yield break;
            }

            var sdkToken = token();
            var registeredBuildId = gameBuildId();

            if (string.IsNullOrWhiteSpace(sdkToken))
            {
                completed(EvidenceUpload.Failed(
                    "This game is not signed in, so the evidence document cannot be uploaded."));
                yield break;
            }

            // 등록 응답이 gameBuildId 를 주지 않았거나 아직 등록하지 않았다. 어느 빌드에 붙일지를 모르는 채로 올리면 문서는
            // 아무 데도 앉지 못하므로, 올리기 전에 그렇다고 말한다.
            if (string.IsNullOrWhiteSpace(registeredBuildId))
            {
                completed(EvidenceUpload.Failed(
                    "This game is not registered to a build, so there is nowhere to attach the evidence document."));
                yield break;
            }

            EvidenceUploadTicketResponseDto ticket = null;
            string failure = null;

            using (var ticketRequest = CreateTicketRequest(sdkToken, registeredBuildId, document.LongLength))
            {
                yield return ticketRequest.SendWebRequest();

                if (ticketRequest.result != UnityWebRequest.Result.Success)
                {
                    failure = Describe("The evidence upload was refused", ticketRequest);
                }
                else
                {
                    ticket = Read<EvidenceUploadTicketResponseDto>(ticketRequest, ref failure);

                    if (failure == null && (ticket == null || string.IsNullOrEmpty(ticket.UploadUrl)))
                    {
                        failure = "The evidence upload ticket came back without an upload URL.";
                    }
                }
            }

            if (failure != null)
            {
                completed(EvidenceUpload.Failed(failure));
                yield break;
            }

            using (var put = CreatePutRequest(ticket, document))
            {
                yield return put.SendWebRequest();

                if (put.result != UnityWebRequest.Result.Success)
                {
                    completed(EvidenceUpload.Failed(Describe("The evidence document upload failed", put)));
                    yield break;
                }
            }

            RegisterEvidenceDocumentResponseDto registration = null;

            using (var register = CreateRegisterRequest(sdkToken, registeredBuildId, ticket.ObjectKey))
            {
                yield return register.SendWebRequest();

                if (register.result != UnityWebRequest.Result.Success)
                {
                    // 바이트는 스토리지에 올라갔지만 어느 빌드의 것인지 아무도 모른다. 성공으로 답하면 서버의 씬 명세 표는
                    // 비어 있는데 SDK 만 올렸다고 믿는, 정확히 이 이슈가 고치려는 그 상태가 된다.
                    completed(EvidenceUpload.Failed(
                        Describe("The evidence document was stored but could not be registered", register)));
                    yield break;
                }

                registration = Read<RegisterEvidenceDocumentResponseDto>(register, ref failure);
            }

            if (failure != null)
            {
                completed(EvidenceUpload.Failed(failure));
                yield break;
            }

            if (registration == null)
            {
                completed(EvidenceUpload.Failed("The evidence registration came back empty."));
                yield break;
            }

            completed(new EvidenceUpload
            {
                ObjectKey = ticket.ObjectKey,
                EvidenceDigest = registration.EvidenceDigest,
                ByteSize = registration.ByteSize,
                SchemaVersion = registration.SchemaVersion,
                AlreadyRegistered = registration.AlreadyRegistered
            });
        }

        private UnityWebRequest CreateTicketRequest(
            string sdkToken, string registeredBuildId, long contentLength)
        {
            return CreatePostRequest(
                sdkToken,
                ContentMapPath(registeredBuildId) + "/ticket",
                new EvidenceUploadTicketRequestDto { ContentLength = contentLength });
        }

        private UnityWebRequest CreateRegisterRequest(
            string sdkToken, string registeredBuildId, string objectKey)
        {
            return CreatePostRequest(
                sdkToken,
                ContentMapPath(registeredBuildId),
                new RegisterEvidenceDocumentRequestDto { ObjectKey = objectKey });
        }

        private UnityWebRequest CreatePostRequest(string sdkToken, string path, object body)
        {
            var endpoint = new Uri(server().HttpBaseUri, path);
            var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonCodec.Serialize(body))),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + sdkToken);
            return request;
        }

        private static UnityWebRequest CreatePutRequest(
            EvidenceUploadTicketResponseDto ticket, byte[] document)
        {
            var put = new UnityWebRequest(ticket.UploadUrl, UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerRaw(document),
                downloadHandler = new DownloadHandlerBuffer()
            };

            // 서명이 이 헤더들을 덮는다. 다른 Content-Type 을 보내거나 하나를 빠뜨리면 스토리지는 잘못된 것을 저장하는 대신
            // PUT 을 거절한다.
            if (ticket.RequiredHeaders != null)
            {
                foreach (var header in ticket.RequiredHeaders)
                {
                    put.SetRequestHeader(header.Key, header.Value);
                }
            }

            return put;
        }

        private static string ContentMapPath(string registeredBuildId)
        {
            return string.Format(ContentMapPathFormat, Uri.EscapeDataString(registeredBuildId));
        }

        /// <summary>
        /// 응답 본문을 읽되, 읽지 못한 이유도 실패로 남긴다.
        /// </summary>
        /// <remarks>
        /// 200 을 받고도 본문을 못 읽는 경우가 조용히 성공으로 지나가면, 실패한 업로드가 성공으로 보고된다.
        /// </remarks>
        private TDto Read<TDto>(UnityWebRequest request, ref string failure)
            where TDto : class
        {
            try
            {
                return jsonCodec.Deserialize<TDto>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                failure = "The evidence upload response could not be read: " + exception.Message;
                return null;
            }
        }

        private static string Describe(string what, UnityWebRequest request)
        {
            var body = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            return string.IsNullOrWhiteSpace(body)
                ? what + " (HTTP " + request.responseCode + ")."
                : what + " (HTTP " + request.responseCode + "): " + body;
        }
    }
}
