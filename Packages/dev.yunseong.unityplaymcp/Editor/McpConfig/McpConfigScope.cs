namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정을 Unity project 하나에 적용할지, 이 계정의 모든 project 에 적용할지.
    /// </summary>
    /// <remarks>
    /// 설정 파일 경로를 고르는 데만 쓴다. scope 를 바꿔도 이미 쓴 설정은 옮기거나 지우지 않는다.
    /// </remarks>
    internal enum McpConfigScope
    {
        /// <summary>Unity project 디렉터리 아래. 이 project 를 여는 agent 만 본다.</summary>
        Project = 0,

        /// <summary>사용자 홈 디렉터리 아래. 이 계정에서 여는 모든 project 가 본다.</summary>
        User = 1,
    }
}
