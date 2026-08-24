using System;
using System.Collections;
using System.IO;
using Artel.Affordances.Scan;

namespace Artel.Evidence
{
    /// <summary>
    /// 스캔이 내놓은 근거 문서, 또는 내놓지 못한 이유.
    /// </summary>
    internal struct ScannedEvidence
    {
        public byte[] Document;

        /// <summary>문서가 서술하는 씬 수. 무엇이 올라가는지를 결과에 적기 위한 것.</summary>
        public int SceneCount;

        /// <summary>스캔이 성공했으면 null.</summary>
        public string Error;

        public bool IsSuccess { get { return Error == null; } }

        public static ScannedEvidence Failed(string error)
        {
            return new ScannedEvidence { Error = error };
        }
    }

    internal interface IEvidenceScan
    {
        IEnumerator Run(Action<ScannedEvidence> completed);
    }

    /// <summary>
    /// 빌드의 모든 씬을 돌아 근거 문서를 짓고, 그것을 바이트로 읽어 준다.
    /// </summary>
    /// <remarks>
    /// 스캔 자체는 새로 쓰지 않는다 — <see cref="AffordanceBootstrap.WalkAllScenes"/> 가 이미 씬을 하나씩 띄우고 읽고
    /// 마지막에 저장한다. 여기가 더하는 것은 두 가지뿐이다: 그 순회가 끝날 때까지 기다리는 것과, 끝난 자리에서 파일을 읽는 것.
    ///
    /// 로드된 씬만 읽는 <c>CaptureNow</c> 가 아니라 순회를 부른다. 서버가 청하는 것은 이 빌드의 씬 명세 표이고, 지금 화면에
    /// 올라와 있는 씬 하나로는 그 표가 채워지지 않는다.
    ///
    /// 파일을 통째로 읽는 것이 이 규모에서 문제가 되지 않는다: <c>AffordanceReport.Compose()</c> 가 이미 문서 전체를 한
    /// 문자열로 지어 <c>File.WriteAllText</c> 에 넘기므로 오늘도 이미 통째로 메모리에 올라와 있고, 실측 1,413 KB 는 캡처 한
    /// 장의 원시 픽셀보다 작다.
    /// </remarks>
    internal sealed class WalkedEvidenceScan : IEvidenceScan
    {
        public IEnumerator Run(Action<ScannedEvidence> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            // 순회는 진행 중이던 실행을 버리고 씬을 갈아 끼운다. 둘이 동시에 돌면 어느 씬이 로드될지를 두고 다투므로,
            // 이미 도는 것이 있으면 겹쳐 돌지 않고 그렇다고 답한다 — 에디터 메뉴에서 시작한 순회도 여기에 걸린다.
            if (AffordanceBootstrap.Walking)
            {
                completed(ScannedEvidence.Failed("A scene walk is already running, so this scan was not started."));
                yield break;
            }

            if (!AffordanceBootstrap.WalkAllScenes())
            {
                completed(ScannedEvidence.Failed(
                    "The scene walk would not start. It needs play mode: scenes are read as the game loads them."));
                yield break;
            }

            while (AffordanceBootstrap.Walking)
            {
                yield return null;
            }

            var path = AffordanceBootstrap.ReportPath;
            byte[] document;

            try
            {
                document = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                // 순회는 끝났는데 문서가 없다. 디스크가 꽉 찼거나 읽기 전용이거나 — 어느 쪽이든 조용히 성공으로 넘기면
                // 서버는 영원히 기다린다.
                completed(ScannedEvidence.Failed(
                    "The evidence document at " + path + " could not be read: " + exception.Message));
                yield break;
            }

            completed(new ScannedEvidence
            {
                Document = document,
                SceneCount = AffordanceReport.SceneCount
            });
        }
    }
}
