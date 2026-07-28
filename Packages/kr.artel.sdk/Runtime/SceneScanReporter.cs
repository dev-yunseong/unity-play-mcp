using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// 등록 시점의 씬 구성을 서버에 보고할 형태로 만든다.
    ///
    /// 내용 스캔은 <see cref="AllSceneScanner"/>에 맡긴다. 런타임 scan_all_scenes와
    /// 같은 씬 워크를 쓰므로 잔여 오브젝트 정리와 활성 씬 복구가 함께 따라온다.
    /// 씬을 실제로 로드하는 비동기 작업이라 코루틴으로만 실행할 수 있다.
    /// </summary>
    internal static class SceneScanReporter
    {
        /// <param name="onProgress">
        /// 씬 하나를 볼 때마다 (1부터 세는 현재 씬, 전체 씬 수)를 받는다. 씬 워크가 화면을
        /// 점유하는 동안 진행 상황을 보여 주려는 호출자를 위한 것이다.
        /// </param>
        public static IEnumerator CreateReport(
            Action<SceneScanReportDto> onCompleted,
            Action<int, int> onProgress = null)
        {
            if (onCompleted == null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }

            var report = ListBuildScenes();

            // 스캔은 부가 정보다. 씬 워크가 어디서 터지더라도 등록 자체를 막으면 안 되므로
            // 직접 돌리면서 예외를 삼킨다. 터지기 전까지 담은 씬은 그대로 보고에 남는다.
            var walk = ScanEveryScene(report, onProgress);
            while (true)
            {
                object current;
                try
                {
                    if (!walk.MoveNext())
                    {
                        break;
                    }

                    current = walk.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Artel: 전체 씬 스캔이 중단되어 스캔한 데까지만 보고합니다. " + exception.Message);
                    break;
                }

                yield return current;
            }

            onCompleted(report);
        }

        // ActionExecutor가 쓰는 스캐너와 타깃 맵을 공유하면 안 되므로 새 인스턴스로 스캔한다.
        private static IEnumerator ScanEveryScene(SceneScanReportDto report, Action<int, int> onProgress)
        {
            List<ScannedSceneDto> scanned = null;
            yield return new AllSceneScanner(new SceneScanner())
                .ScanAll(SceneScanOptions.Default, result => scanned = result, onProgress);

            foreach (var scene in scanned)
            {
                report.ScannedScenes.Add(SceneScanReportMapper.ToReport(scene.Scene));
            }
        }

        // 목록을 못 만들어도 등록을 막을 이유는 없다.
        private static SceneScanReportDto ListBuildScenes()
        {
            var report = new SceneScanReportDto();
            try
            {
                for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    report.ScenesInBuild.Add(SceneUtility.GetScenePathByBuildIndex(i));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Artel: Build Settings의 씬 목록을 읽지 못했습니다. " + exception.Message);
            }

            return report;
        }
    }
}
