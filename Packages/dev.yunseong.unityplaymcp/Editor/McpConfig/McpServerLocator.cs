using System;
using System.Reflection;
using UnityEditor.PackageManager;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// 이 기계에서 빌드된 MCP server 의 entry point 를 찾는다.
    /// </summary>
    /// <remarks>
    /// server 는 Unity package 안이 아니라 저장소의 <c>mcp/</c> 에 있고, 절대경로는 기계마다 다르다.
    /// 그래서 설정 파일에 넣을 값을 저장소에 적어 둘 수 없고 실행 시점에 찾아야 한다.
    /// </remarks>
    internal static class McpServerLocator
    {
        private const string EntryPointFromRoot = "mcp/dist/index.js";

        /// <summary>package 가 놓인 실제 디렉터리. package 정보를 얻지 못하면 <c>null</c>.</summary>
        internal static string PackageRoot()
        {
            return PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly())?.resolvedPath;
        }

        /// <summary>
        /// 후보 두 자리를 차례로 본다.
        /// </summary>
        /// <remarks>
        /// 첫째는 package 의 조부모다. package 가 저장소의 <c>Packages/</c> 에 놓였을 때 — embedded 이든
        /// 샘플 프로젝트의 <c>file:</c> 참조이든 — 거기가 저장소 루트다. 둘째는 Unity project 루트로, Unity
        /// project 자체가 저장소 루트인 경우다. 둘 다 아니면 (예를 들어 package 만 git URL 로 설치했으면)
        /// 이 기계에 server 가 없는 것이므로 <c>null</c> 을 돌려준다.
        /// </remarks>
        internal static string FindEntryPoint(string packageRoot, string projectRoot, Func<string, bool> fileExists)
        {
            var repositoryRoot = GrandparentOf(packageRoot);

            if (repositoryRoot != null)
            {
                var candidate = EntryPointUnder(repositoryRoot);

                if (fileExists(candidate))
                {
                    return candidate;
                }
            }

            if (!string.IsNullOrEmpty(projectRoot))
            {
                var candidate = EntryPointUnder(projectRoot);

                if (fileExists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 설정 파일에 적힐 값이므로 구분자를 <c>/</c> 로 맞춘다. node 는 Windows 에서도 이 형태를 받고,
        /// backslash 를 escape 한 경로보다 사람이 읽기 쉽다.
        /// </summary>
        private static string EntryPointUnder(string root)
        {
            return root.Replace('\\', '/').TrimEnd('/') + "/" + EntryPointFromRoot;
        }

        /// <summary>
        /// 두 단계 위 디렉터리. 더 올라갈 곳이 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// <c>Path.GetFullPath</c> 를 쓰지 않는다. Windows 에서 <c>/repo/Packages/pkg</c> 처럼 슬래시로 시작하는
        /// 경로에 현재 드라이브 문자를 붙여 버려서, 받은 경로와 다른 문자열이 나온다. <c>resolvedPath</c> 는
        /// 이미 절대경로이므로 문자열로 잘라내는 것으로 충분하다.
        /// </remarks>
        private static string GrandparentOf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var walked = path.Replace('\\', '/').TrimEnd('/');

            for (var level = 0; level < 2; level++)
            {
                var separator = walked.LastIndexOf('/');

                if (separator <= 0)
                {
                    return null;
                }

                walked = walked.Substring(0, separator);
            }

            return walked;
        }
    }
}
