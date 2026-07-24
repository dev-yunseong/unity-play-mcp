using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// 등록 시점의 씬 구성을 서버에 보고할 형태로 만든다.
    ///
    /// 런타임에서는 로드된 씬만 내용을 스캔할 수 있다. 로드되지 않은 씬까지 강제로
    /// 로드하면 Awake/Start가 실행되어 게임 상태를 건드리므로 하지 않는다. 대신
    /// Build Settings의 씬 경로 목록으로 전체 씬의 존재만 보고한다.
    /// </summary>
    internal static class SceneScanReporter
    {
        public static SceneScanReportDto CreateReport()
        {
            var report = new SceneScanReportDto();

            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                report.ScenesInBuild.Add(SceneUtility.GetScenePathByBuildIndex(i));
            }

            // ActionExecutor가 쓰는 스캐너와 타깃 맵을 공유하면 안 되므로 새 인스턴스로 스캔한다.
            var scanner = new SceneScanner();
            foreach (var scene in scanner.ScanLoadedScenes())
            {
                report.ScannedScenes.Add(SceneScanReportMapper.ToReport(SceneSnapshotMapper.ToDto(scene)));
            }

            return report;
        }
    }
}
