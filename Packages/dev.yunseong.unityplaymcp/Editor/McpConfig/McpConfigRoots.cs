using System;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일 자리를 계산하는 데 필요한 뿌리 경로와 운영체제.
    /// </summary>
    /// <remarks>
    /// <see cref="McpAgent.Catalog"/> 가 받는 값을 한 type 으로 묶는다. 네 개를 낱개 인자로 넘기면 부르는 쪽마다
    /// 순서를 틀릴 수 있고, 어느 것이 없어도 되는 값인지 signature 만 보고는 알 수 없다.
    /// <see cref="RoamingApplicationDataDirectory"/> 는 Windows 의 <c>%APPDATA%</c> 이고 다른 운영체제에서는
    /// 쓰지 않으므로 비어 있어도 된다.
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
