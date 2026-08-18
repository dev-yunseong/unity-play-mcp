using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// discovery 가 살아남지 못할 빌드라면 빌드 전에 그렇다고 말한다.
    /// </summary>
    /// <remarks>
    /// managed stripping 을 High 로 두면 이 패키지가 플레이어에서 빠진다: 샘플 프로젝트에서 실측하니 스캔 어셈블리도,
    /// attribute 도, 그것을 선언하는 어셈블리도 전부 빌드에 없었고 근거 리소스만 남았다. 그것을 읽을 것도 남지 않고
    /// 불평할 것도 남지 않으므로, 게임은 돌고, 리포트를 쓰지 않으며, 스캔이 아무것도 찾지 못한 게임과 똑같아 보인다.
    ///
    /// 이것은 에디터에서 도는데, 두 가지를 아직 다 아는 마지막 자리가 거기다 — discovery 가 켜져 있다는 것과, 곧
    /// 만들어질 빌드가 그것을 없애리라는 것.
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

            Debug.LogWarning(
                "[Artel] Managed stripping is set to High, which removes this package from the " +
                "build. The game will run and write no report. Lower the stripping level, or turn " +
                "discovery off under Artel / Discovery so the absence is intended.");
        }
    }
}
