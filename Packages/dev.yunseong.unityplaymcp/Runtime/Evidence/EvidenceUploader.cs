using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;
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

        /// <summary>서버에 붙은 씬 이미지 수. 캡처를 아예 안 보낸 실행에서는 0 이다.</summary>
        public int SceneCapturesRegistered;

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
        /// <param name="thumbnails">씬 대표 이미지들. null 이거나 비어 있으면 캡처 없이 등록한다.</param>
        IEnumerator Upload(
            byte[] document, IReadOnlyList<SceneThumbnail> thumbnails, Action<EvidenceUpload> completed);
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

        /// <summary>서버가 씬 이미지에 대해 받는 유일한 형식.</summary>
        private const string SceneCaptureContentType = "image/jpeg";

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

        public IEnumerator Upload(
            byte[] document, IReadOnlyList<SceneThumbnail> thumbnails, Action<EvidenceUpload> completed)
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

            // 문서가 스토리지에 앉은 뒤, 등록 전. 캡처의 objectKey 는 등록 body 에 실려야 하므로 그 전에 올라가 있어야 하고,
            // 등록보다 앞서야 실패했을 때 아무것도 붙지 않은 상태로 끝난다.
            var captures = new List<SceneCaptureRegistrationDto>();
            yield return UploadThumbnails(sdkToken, registeredBuildId, thumbnails, captures);

            RegisterEvidenceDocumentResponseDto registration = null;

            using (var register = CreateRegisterRequest(sdkToken, registeredBuildId, ticket.ObjectKey, captures))
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
                SceneCapturesRegistered = captures.Count,
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
            string sdkToken,
            string registeredBuildId,
            string objectKey,
            List<SceneCaptureRegistrationDto> captures)
        {
            return CreatePostRequest(
                sdkToken,
                ContentMapPath(registeredBuildId),
                new RegisterEvidenceDocumentRequestDto
                {
                    ObjectKey = objectKey,

                    // 빈 목록은 보내지 않는다. 이 절을 모르는 서버가 받던 것과 글자 그대로 같은 body 가 된다.
                    SceneCaptures = captures.Count == 0 ? null : captures
                });
        }

        /// <summary>
        /// 씬 이미지를 올리고, 등록에 실을 항목을 <paramref name="captures"/> 에 채운다.
        /// </summary>
        /// <remarks>
        /// <b>여기서는 아무것도 실패하지 않는다.</b> 이미지는 근거 문서의 곁다리다. 티켓을 못 받았든, PUT 이 거절됐든,
        /// 서버가 이 경로 자체를 모르든(404) — 그 실행의 근거 문서는 여전히 올라가야 한다. 이미지가 없는 씬 명세는 덜 친절할
        /// 뿐이지만, 근거가 없는 빌드는 아무것도 아니다.
        ///
        /// 그래서 실패한 씬은 <c>failureCode</c> 로 적어 함께 보낸다. 조용히 빼면 화면은 "SDK 가 안 찍었다"와 "찍었는데 못
        /// 올렸다"를 구분할 수 없다.
        /// </remarks>
        private IEnumerator UploadThumbnails(
            string sdkToken,
            string registeredBuildId,
            IReadOnlyList<SceneThumbnail> thumbnails,
            List<SceneCaptureRegistrationDto> captures)
        {
            if (thumbnails == null || thumbnails.Count == 0)
            {
                yield break;
            }

            var wanted = new List<SceneThumbnail>();

            for (var index = 0; index < thumbnails.Count; index++)
            {
                var thumbnail = thumbnails[index];

                if (string.IsNullOrWhiteSpace(thumbnail.SceneName))
                {
                    continue;
                }

                if (thumbnail.IsSuccess)
                {
                    wanted.Add(thumbnail);
                }
                else
                {
                    // 못 찍은 것은 올릴 것이 없다. 사실만 등록에 싣는다.
                    captures.Add(new SceneCaptureRegistrationDto
                    {
                        SceneName = thumbnail.SceneName,
                        FailureCode = thumbnail.FailureCode ?? "capture-failed"
                    });
                }
            }

            if (wanted.Count == 0)
            {
                yield break;
            }

            SceneCaptureTicketBatchResponseDto batch = null;

            using (var request = CreateSceneCaptureTicketRequest(sdkToken, registeredBuildId, wanted))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 404 면 이 서버는 씬 이미지를 아직 모른다. 그것도 못 올린 이유이므로 같은 자리에 적는다.
                    var reason = request.responseCode == 404 ? "server-has-no-capture-endpoint" : "upload-refused";
                    Debug.LogWarning(
                        "[Artel] Scene captures were not uploaded (HTTP " + request.responseCode + ").");
                    MarkAll(wanted, captures, reason);
                    yield break;
                }

                string failure = null;
                batch = Read<SceneCaptureTicketBatchResponseDto>(request, ref failure);

                if (failure != null || batch == null || batch.Captures == null)
                {
                    MarkAll(wanted, captures, "upload-refused");
                    yield break;
                }
            }

            var tickets = new Dictionary<string, SceneCaptureUploadTicketDto>();

            for (var index = 0; index < batch.Captures.Count; index++)
            {
                var ticket = batch.Captures[index];

                if (ticket != null && !string.IsNullOrEmpty(ticket.SceneName) &&
                    !string.IsNullOrEmpty(ticket.UploadUrl))
                {
                    tickets[ticket.SceneName] = ticket;
                }
            }

            for (var index = 0; index < wanted.Count; index++)
            {
                var thumbnail = wanted[index];

                if (!tickets.TryGetValue(thumbnail.SceneName, out var ticket))
                {
                    // 청한 씬에 티켓이 안 왔다. 서버가 뭔가 걸렀다는 뜻이고, 그 씬은 이미지 없이 등록된다.
                    captures.Add(Failed(thumbnail, "no-ticket"));
                    continue;
                }

                using (var put = CreateCapturePutRequest(ticket, thumbnail.Jpeg))
                {
                    yield return put.SendWebRequest();

                    if (put.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning(
                            "[Artel] The screen for " + thumbnail.SceneName + " could not be stored (HTTP " +
                            put.responseCode + ").");
                        captures.Add(Failed(thumbnail, "upload-failed"));
                        continue;
                    }
                }

                captures.Add(new SceneCaptureRegistrationDto
                {
                    SceneName = thumbnail.SceneName,
                    ObjectKey = ticket.ObjectKey,
                    ContentType = SceneCaptureContentType,
                    Width = thumbnail.Width,
                    Height = thumbnail.Height
                });
            }
        }

        private static void MarkAll(
            List<SceneThumbnail> thumbnails, List<SceneCaptureRegistrationDto> captures, string failureCode)
        {
            for (var index = 0; index < thumbnails.Count; index++)
            {
                captures.Add(Failed(thumbnails[index], failureCode));
            }
        }

        private static SceneCaptureRegistrationDto Failed(SceneThumbnail thumbnail, string failureCode)
        {
            return new SceneCaptureRegistrationDto
            {
                SceneName = thumbnail.SceneName,
                FailureCode = failureCode
            };
        }

        private UnityWebRequest CreateSceneCaptureTicketRequest(
            string sdkToken, string registeredBuildId, List<SceneThumbnail> thumbnails)
        {
            var requested = new List<SceneCaptureTicketRequestDto>();

            for (var index = 0; index < thumbnails.Count; index++)
            {
                var thumbnail = thumbnails[index];
                requested.Add(new SceneCaptureTicketRequestDto
                {
                    SceneName = thumbnail.SceneName,
                    ContentType = SceneCaptureContentType,
                    ContentLength = thumbnail.Jpeg.LongLength,
                    Width = thumbnail.Width,
                    Height = thumbnail.Height
                });
            }

            return CreatePostRequest(
                sdkToken,
                ContentMapPath(registeredBuildId) + "/scene-captures/tickets",
                new SceneCaptureTicketBatchRequestDto { Captures = requested });
        }

        private static UnityWebRequest CreateCapturePutRequest(SceneCaptureUploadTicketDto ticket, byte[] jpeg)
        {
            var put = new UnityWebRequest(ticket.UploadUrl, UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerRaw(jpeg),
                downloadHandler = new DownloadHandlerBuffer()
            };

            if (ticket.RequiredHeaders != null)
            {
                foreach (var header in ticket.RequiredHeaders)
                {
                    put.SetRequestHeader(header.Key, header.Value);
                }
            }

            return put;
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
