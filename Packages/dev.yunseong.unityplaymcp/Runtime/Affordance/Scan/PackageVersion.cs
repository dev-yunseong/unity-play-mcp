namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 이 package 의 version. runtime 이 자기 version 을 말해야 하는 모든 자리가 여기를 읽는다.
    /// </summary>
    /// <remarks>
    /// <c>package.json</c> 의 <c>version</c> 과 손으로 맞춘 값이다. 자동으로 채울 방법이 없다 — player
    /// build 에 <c>package.json</c> 이 들어가지 않고, <c>UnityEditor.PackageManager</c> 로 읽으면 runtime
    /// assembly 가 editor 전용 API 에 걸려 Standalone build 가 깨진다. git tag 도 답이 아니다. Unity
    /// Package Manager 는 git URL 설치에서 저장소에 commit 된 <c>package.json</c> 을 그대로 읽고, 그 사이에
    /// 값을 채워 넣을 build 단계가 없다. tag 는 <c>package.json</c> 에서 파생되는 쪽이고, 그 일치는
    /// <c>publish-mcp.yml</c> 이 release 때 확인한다.
    ///
    /// 그래서 손으로 맞추는 값이 하나는 남는다. 남는 것이 하나뿐이도록 여기 모았고, 어긋나면
    /// <c>PackageVersionTests</c> 가 잡는다. 이 자리에 있는 이유는 assembly 의존 방향 때문이다 —
    /// <c>UnityPlayMcp.Runtime</c> 이 이 assembly 를 참조하므로 양쪽 소비자가 모두 여기를 볼 수 있다.
    /// </remarks>
    internal static class PackageVersion
    {
        internal const string Value = "0.2.0";
    }
}
