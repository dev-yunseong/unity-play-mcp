using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Editor
{
    /// <summary>
    /// managed stripping 이 High 이면 빌드 전에 경고한다.
    /// </summary>
    /// <remarks>
    /// High 는 스캔 assembly 와 attribute 를 빌드에서 제거한다. 그러면 게임은 오류 없이 돌지만 리포트를 쓰지 않아
    /// 스캔이 아무것도 찾지 못한 것과 구분되지 않는다. player 에는 이를 알릴 코드가 남지 않으므로 에디터에서 경고한다.
    /// </remarks>
    internal sealed class ManagedStrippingWarning : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(report.summary.platform));

            if (PlayerSettings.GetManagedStrippingLevel(target) != ManagedStrippingLevel.High)
            {
                return;
            }

            // discovery 를 끄는 설정은 없으므로 stripping level 을 낮추라고만 안내한다.
            Debug.LogWarning(
                "[Unity Play MCP] Managed stripping is set to High, which removes this package from the " +
                "build. The game will run and write no report. Lower the stripping level to keep " +
                "it, or expect no readings from this build.");
        }
    }
}
