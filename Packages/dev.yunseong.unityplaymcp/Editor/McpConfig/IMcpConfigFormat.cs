namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// agent 설정 파일의 텍스트를 받아 텍스트를 돌려준다.
    /// </summary>
    /// <remarks>
    /// 파일을 직접 열지 않으므로 disk 없이 테스트한다. 읽을 수 없는 텍스트에는 예외를 던지고, 호출자가 잡아
    /// 화면에 error 를 띄운 뒤 파일을 그대로 둔다. 사람이 손으로 쓴 설정을 이 기능이 덮어써 없애는 것이
    /// 여기서 낼 수 있는 가장 나쁜 결과다.
    /// </remarks>
    internal interface IMcpConfigFormat
    {
        /// <summary>이 이름의 server 가 이미 적혀 있는지.</summary>
        bool Contains(string text, string serverName);

        /// <summary>이 이름의 server 를 적는다. 이미 있으면 갈아 끼운다.</summary>
        string Add(string text, string serverName, McpServerEntry entry);

        /// <summary>이 이름의 server 를 지운다. 없으면 텍스트를 그대로 돌려준다.</summary>
        string Remove(string text, string serverName);
    }
}
