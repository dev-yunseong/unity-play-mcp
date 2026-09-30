namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 이 package 의 version. runtime 에서 version 이 필요한 곳은 모두 여기를 읽는다.
    /// </summary>
    /// <remarks>
    /// <c>package.json</c> 의 <c>version</c> 과 손으로 맞춘다. player build 에는 <c>package.json</c> 이 없고,
    /// <c>UnityEditor.PackageManager</c> 를 쓰면 Standalone build 가 깨진다.
    /// 어긋나면 <c>PackageVersionTests</c> 가 잡는다. <c>UnityPlayMcp.Runtime</c> 이 이 assembly 를 참조하므로 여기 둔다.
    /// </remarks>
    internal static class PackageVersion
    {
        internal const string Value = "0.4.0";
    }
}
