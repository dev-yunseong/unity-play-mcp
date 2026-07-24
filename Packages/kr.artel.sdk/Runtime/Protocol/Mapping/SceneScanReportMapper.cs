using System.Collections.Generic;
using Artel.Protocol.Dto;

namespace Artel.Protocol.Mapping
{
    /// <summary>
    /// 스트리밍용 씬 DTO를 등록 보고용 DTO로 옮긴다. 유일한 차이는 인스턴스 ID를 버리는 것이다.
    /// 컴포넌트 매핑은 <see cref="SceneSnapshotMapper"/>가 만든 결과를 그대로 재사용한다.
    /// </summary>
    internal static class SceneScanReportMapper
    {
        public static SceneScanSceneDto ToReport(SceneDto scene)
        {
            var children = new List<SceneScanBlockDto>(scene.Children.Count);
            foreach (var child in scene.Children)
            {
                children.Add(ToReport(child));
            }

            return new SceneScanSceneDto
            {
                Type = scene.Type,
                Name = scene.Name,
                Children = children
            };
        }

        private static SceneScanBlockDto ToReport(SceneBlockDto block)
        {
            var children = new List<SceneScanBlockDto>(block.Children.Count);
            foreach (var child in block.Children)
            {
                children.Add(ToReport(child));
            }

            return new SceneScanBlockDto
            {
                Type = block.Type,
                Name = block.Name,
                Components = block.Components,
                Children = children
            };
        }
    }
}
