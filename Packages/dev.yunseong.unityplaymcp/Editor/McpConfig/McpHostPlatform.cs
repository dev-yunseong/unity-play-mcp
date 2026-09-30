namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// user-level 설정 파일 경로를 정하는 운영체제.
    /// </summary>
    /// <remarks>
    /// 호출자가 <c>Application.platform</c> 을 이 값으로 바꿔 넘기므로 catalog 를 host 운영체제와 무관하게 테스트한다.
    /// </remarks>
    internal enum McpHostPlatform
    {
        Windows = 0,
        MacOs = 1,
        Linux = 2,
    }
}
