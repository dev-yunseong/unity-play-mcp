namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// 설정을 어느 자리에 쓸지. Unity project 하나에만 적용할지, 이 계정의 모든 project 에 적용할지.
    /// </summary>
    /// <remarks>
    /// 이 값은 agent 마다 다른 설정 파일 자리를 고르는 데만 쓴다. scope 를 바꾼다고 이미 쓰여 있는 설정이
    /// 옮겨지거나 지워지지는 않는다.
    /// </remarks>
    internal enum McpConfigScope
    {
        /// <summary>Unity project 디렉터리 아래. 이 project 를 여는 agent 만 본다.</summary>
        Project = 0,

        /// <summary>사용자 홈 디렉터리 아래. 이 계정에서 여는 모든 project 가 본다.</summary>
        User = 1,
    }
}
