namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// user-level 설정 파일 자리를 가르는 운영체제.
    /// </summary>
    /// <remarks>
    /// Visual Studio Code 만 세 운영체제에서 서로 다른 자리를 쓴다. <c>Application.platform</c>
    /// 을 여기서 받지 않고 부르는 쪽이 이 값으로 바꿔 넘겨야, catalog 를 host 운영체제와 무관하게 테스트한다.
    /// </remarks>
    internal enum McpHostPlatform
    {
        Windows = 0,
        MacOs = 1,
        Linux = 2,
    }
}
