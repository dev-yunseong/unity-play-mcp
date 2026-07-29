using System;
using System.Collections;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine.Networking;

namespace Artel.Capture
{
    /// <summary>
    /// Where an uploaded capture ended up, or why it did not.
    /// </summary>
    internal struct CaptureUpload
    {
        public string CaptureId;
        public string Url;
        public string ExpiresAt;

        /// <summary>Null when the upload succeeded.</summary>
        public string Error;

        public bool IsSuccess { get { return Error == null; } }

        public static CaptureUpload Failed(string error)
        {
            return new CaptureUpload { Error = error };
        }
    }

    internal interface ICaptureUploader
    {
        IEnumerator Upload(
            CapturedImage image,
            CaptureRequest request,
            Action<CaptureUpload> completed);
    }

    /// <summary>
    /// Gets a signed URL from orchestration and PUTs the bytes to storage directly.
    /// </summary>
    /// <remarks>
    /// Two hops rather than one because the image must not travel through orchestration: that
    /// server records relayed frames into the QA log and republishes them over SSE, so routing
    /// megabytes through it would grow the database and the stream with every capture.
    ///
    /// Nothing is retried. A capture that failed to upload is a failed action, and whether to try
    /// again is a judgement the agent makes with the scenario in hand — not one this client can
    /// make by counting attempts.
    /// </remarks>
    internal sealed class CaptureUploader : ICaptureUploader
    {
        private const string TicketPath = "/api/sdk/qa-captures/tickets";

        private readonly IJsonCodec jsonCodec;
        private readonly Func<Server> server;
        private readonly Func<string> instanceKey;

        public CaptureUploader(IJsonCodec jsonCodec, Func<Server> server, Func<string> instanceKey)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.instanceKey = instanceKey ?? throw new ArgumentNullException(nameof(instanceKey));
        }

        public IEnumerator Upload(
            CapturedImage image,
            CaptureRequest request,
            Action<CaptureUpload> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            var key = instanceKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                completed(CaptureUpload.Failed(
                    "This game has no instance key, so a capture cannot be uploaded."));
                yield break;
            }

            CaptureTicketResponseDto ticket = null;
            string ticketError = null;
            using (var ticketRequest = CreateTicketRequest(key, image, request))
            {
                yield return ticketRequest.SendWebRequest();

                if (ticketRequest.result != UnityWebRequest.Result.Success)
                {
                    ticketError = Describe("The capture upload was refused", ticketRequest);
                }
                else
                {
                    ticket = jsonCodec.Deserialize<CaptureTicketResponseDto>(
                        ticketRequest.downloadHandler.text);
                    if (ticket == null || string.IsNullOrEmpty(ticket.UploadUrl))
                    {
                        ticketError = "The capture ticket came back without an upload URL.";
                    }
                }
            }

            if (ticketError != null)
            {
                completed(CaptureUpload.Failed(ticketError));
                yield break;
            }

            using (var put = CreatePutRequest(ticket, image))
            {
                yield return put.SendWebRequest();

                if (put.result != UnityWebRequest.Result.Success)
                {
                    completed(CaptureUpload.Failed(Describe("The capture upload failed", put)));
                    yield break;
                }
            }

            completed(new CaptureUpload
            {
                CaptureId = ticket.CaptureId,
                Url = ticket.DownloadUrl,
                ExpiresAt = ticket.DownloadExpiresAt
            });
        }

        private UnityWebRequest CreateTicketRequest(
            string key,
            CapturedImage image,
            CaptureRequest request)
        {
            var endpoint = new Uri(server().HttpBaseUri, TicketPath);
            var body = jsonCodec.Serialize(new CaptureTicketRequestDto
            {
                InstanceKey = key,
                ContentType = request.ContentType,
                ContentLength = image.Bytes.LongLength,
                TargetId = request.TargetId
            });
            var ticketRequest = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            ticketRequest.SetRequestHeader("Content-Type", "application/json");
            return ticketRequest;
        }

        private static UnityWebRequest CreatePutRequest(
            CaptureTicketResponseDto ticket,
            CapturedImage image)
        {
            var put = new UnityWebRequest(ticket.UploadUrl, UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerRaw(image.Bytes),
                downloadHandler = new DownloadHandlerBuffer()
            };

            // The signature covers these headers. Sending a different Content-Type, or omitting
            // one, makes storage reject the PUT rather than store the wrong thing.
            if (ticket.RequiredHeaders != null)
            {
                foreach (var header in ticket.RequiredHeaders)
                {
                    put.SetRequestHeader(header.Key, header.Value);
                }
            }

            return put;
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
