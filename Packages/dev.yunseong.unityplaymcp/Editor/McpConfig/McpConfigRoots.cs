using System;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일 경로를 계산하는 데 필요한 root 경로와 운영체제.
    /// </summary>
    /// <remarks>
    /// <see cref="McpAgent.Catalog"/> 의 인자를 묶어 인자 순서 실수를 막고 필수 값을 생성자에서 검사한다.
    /// </remarks>
    internal sealed class McpConfigRoots
    {
        internal McpConfigRoots(
            string projectRoot,
            string homeDirectory,
            McpHostPlatform platform,
            string roamingApplicationDataDirectory = null)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("The Unity project directory is required.", nameof(projectRoot));
            }

            if (string.IsNullOrEmpty(homeDirectory))
            {
                throw new ArgumentException("The home directory is required.", nameof(homeDirectory));
            }

            ProjectRoot = projectRoot;
            HomeDirectory = homeDirectory;
            Platform = platform;
            RoamingApplicationDataDirectory = roamingApplicationDataDirectory;
        }

        /// <summary>Unity project 디렉터리. <c>Assets</c> 의 부모다.</summary>
        internal string ProjectRoot { get; }

        /// <summary>사용자 홈 디렉터리.</summary>
        internal string HomeDirectory { get; }

        internal McpHostPlatform Platform { get; }

        /// <summary>Windows 의 <c>%APPDATA%</c>. 다른 운영체제에서는 <c>null</c> 이어도 된다.</summary>
        internal string RoamingApplicationDataDirectory { get; }
    }
}
