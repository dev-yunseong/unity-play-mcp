using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Evidence;
using Artel.Protocol.Dto;
using Artel.Serialization;
using NUnit.Framework;

namespace Artel.Tests.Evidence
{
    /// <summary>
    /// 서버가 보낸 <c>scan_evidence</c> 가 무엇을 답하는지.
    /// </summary>
    /// <remarks>
    /// 여기서 지키는 것은 실패가 답에 실린다는 것 하나다. 스캔이든 업로드든 조용히 삼켜지면 서버는 "보냈다"까지만 알고
    /// 화면은 영원히 기다리는데, 그것이 정확히 이 이슈 이전의 상태다.
    ///
    /// 씬 순회 자체와 실제 HTTP 세 걸음은 여기서 덮지 못한다. 순회는 플레이 모드에서 씬을 하나씩 띄우는 일이고, 세 걸음은
    /// 서명한 스토리지 URL 을 내주는 서버가 있어야 한다. 그 둘은 손으로 확인한다.
    /// </remarks>
    public sealed class EvidenceScanActionTests
    {
        [Test]
        public void ScanEvidence_RefusesWhenTheBuildCarriesNoScanOrUploader()
        {
            var result = Run(Executor(null, null));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Action, Is.EqualTo("scan_evidence"));
            Assert.That(result.Error, Does.Contain("cannot scan"));
        }

        /// <summary>
        /// 순회가 이미 돌고 있거나 플레이 모드가 아니어서 스캔이 시작조차 못 한 경우. 그 사유가 그대로 서버로 간다.
        /// </summary>
        [Test]
        public void ScanEvidence_CarriesWhyTheScanDidNotRun()
        {
            var uploader = new FakeEvidenceUploader();
            var result = Run(Executor(
                new FakeEvidenceScan(ScannedEvidence.Failed("A scene walk is already running.")),
                uploader));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Action, Is.EqualTo("scan_evidence"));
            Assert.That(result.Error, Does.Contain("already running"));

            // 스캔이 실패했으면 올릴 것이 없다. 빈 문서를 올려 서버의 표를 지우는 것이 조용한 실패의 모양이다.
            Assert.That(uploader.Attempts, Is.EqualTo(0));
        }

        [Test]
        public void ScanEvidence_CarriesWhyTheUploadFailed()
        {
            var result = Run(Executor(
                new FakeEvidenceScan(new ScannedEvidence { Document = new byte[16], SceneCount = 3 }),
                new FakeEvidenceUploader(
                    EvidenceUpload.Failed("The evidence upload was refused (HTTP 409): build not found."))));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Action, Is.EqualTo("scan_evidence"));
            Assert.That(result.Error, Does.Contain("409"));
        }

        [Test]
        public void ScanEvidence_ReportsWhatItUploaded()
        {
            var result = Run(Executor(
                new FakeEvidenceScan(new ScannedEvidence { Document = new byte[16], SceneCount = 3 }),
                new FakeEvidenceUploader(new EvidenceUpload
                {
                    ObjectKey = "content-map/5/abc.json",
                    EvidenceDigest = "sha256:abc",
                    ByteSize = 1_413_000,
                    SchemaVersion = 6
                })));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Action, Is.EqualTo("scan_evidence"));

            var reported = (EvidenceScanResultDto)result.ReturnValue;
            Assert.That(reported.EvidenceDigest, Is.EqualTo("sha256:abc"));
            Assert.That(reported.SceneCount, Is.EqualTo(3));
        }

        // --- wire shape ---

        /// <summary>
        /// 서버가 짝을 맞추는 세 필드. <c>id</c> 만으로는 서버가 스스로 보낸 명령의 결과를 가릴 수 없다.
        /// </summary>
        [Test]
        public void Serialize_CarriesActionSuccessAndError()
        {
            var json = new NewtonsoftJsonCodec().Serialize(
                ActionResultDto.Failure(4, "scan_evidence", "The scene walk would not start."));

            Assert.That(json, Does.Contain("\"action\":\"scan_evidence\""));
            Assert.That(json, Does.Contain("\"success\":false"));
            Assert.That(json, Does.Contain("would not start"));
        }

        /// <summary>
        /// <c>action</c> 은 그것을 채우는 액션에만 붙는다. 나머지 결과는 이 필드가 생기기 전과 같은 바이트여야 한다 —
        /// 릴레이와 에이전트가 이미 그 모양을 파싱하고 있다.
        /// </summary>
        [Test]
        public void Serialize_LeavesAResultWithNoActionNameUnchanged()
        {
            var json = new NewtonsoftJsonCodec().Serialize(ActionResultDto.Success(3));

            Assert.That(json, Does.Not.Contain("action"));
        }

        // --- helpers ---

        private static ActionExecutor Executor(IEvidenceScan scan, IEvidenceUploader uploader)
        {
            var scanner = new SceneScanner();
            scanner.Scan();
            return new ActionExecutor(
                scanner,
                null,
                new PointerEventDispatcher(),
                evidenceScan: scan,
                evidenceUploader: uploader);
        }

        private static ActionResultDto Run(ActionExecutor executor)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(4, "scan_evidence", new List<object>(), value => result = value));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private sealed class FakeEvidenceScan : IEvidenceScan
        {
            private readonly ScannedEvidence scanned;

            public FakeEvidenceScan(ScannedEvidence scanned)
            {
                this.scanned = scanned;
            }

            public IEnumerator Run(Action<ScannedEvidence> completed)
            {
                completed(scanned);
                yield break;
            }
        }

        private sealed class FakeEvidenceUploader : IEvidenceUploader
        {
            private readonly EvidenceUpload upload;

            public FakeEvidenceUploader(EvidenceUpload upload = default)
            {
                this.upload = upload;
            }

            public int Attempts { get; private set; }

            public IEnumerator Upload(byte[] document, Action<EvidenceUpload> completed)
            {
                Attempts++;
                completed(upload);
                yield break;
            }
        }
    }
}
