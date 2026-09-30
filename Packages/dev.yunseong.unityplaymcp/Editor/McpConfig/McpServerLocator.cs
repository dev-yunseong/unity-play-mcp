using System;
using System.Reflection;
using UnityEditor.PackageManager;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 이 기계에서 빌드된 MCP server 의 entry point 를 찾는다.
    /// </summary>
    /// <remarks>
    /// server 는 package 밖 저장소의 <c>mcp/</c> 에 있고 절대경로가 기계마다 다르므로 실행 시점에 찾는다.
    /// </remarks>
    internal static class McpServerLocator
    {
        private const string EntryPointFromRoot = "mcp/dist/index.js";
        private const string NpmPackageName = "unity-play-mcp";

        /// <summary>package 가 놓인 실제 디렉터리. package 정보를 얻지 못하면 <c>null</c>.</summary>
        internal static string PackageRoot()
        {
            return PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly())?.resolvedPath;
        }

        /// <summary>
        /// 로컬 build 를 먼저 찾고, 없으면 version 을 고정한 npm package 실행을 돌려준다.
        /// </summary>
        /// <remarks>
        /// 찾는 순서는 package 의 조부모 (package 가 저장소의 <c>Packages/</c> 에 있을 때), Unity project root,
        /// npm package 순이다. compatible version 을 모르면 latest 를 쓰지 않고 <c>null</c> 을 돌려준다.
        /// </remarks>
        internal static McpServerEntry Resolve(
            string packageRoot,
            string projectRoot,
            string mcpServerVersion,
            Func<string, bool> fileExists)
        {
            var repositoryRoot = GrandparentOf(packageRoot);

            if (repositoryRoot != null)
            {
                var candidate = EntryPointUnder(repositoryRoot);

                if (fileExists(candidate))
                {
                    return LocalEntry(candidate);
                }
            }

            if (!string.IsNullOrEmpty(projectRoot))
            {
                var candidate = EntryPointUnder(projectRoot);

                if (fileExists(candidate))
                {
                    return LocalEntry(candidate);
                }
            }

            if (string.IsNullOrWhiteSpace(mcpServerVersion))
            {
                return null;
            }

            return new McpServerEntry(
                "npx",
                new[] { "-y", NpmPackageName + "@" + mcpServerVersion.Trim() },
                "Using the npm MCP server package because no local build was found.");
        }

        private static McpServerEntry LocalEntry(string entryPoint)
        {
            return new McpServerEntry(
                "node",
                new[] { entryPoint },
                "Using the local MCP server build found in this repository.");
        }

        /// <summary>
        /// 구분자를 <c>/</c> 로 맞춘다. node 는 Windows 에서도 이 형태를 받고 escape 한 경로보다 읽기 쉽다.
        /// </summary>
        private static string EntryPointUnder(string root)
        {
            return root.Replace('\\', '/').TrimEnd('/') + "/" + EntryPointFromRoot;
        }

        /// <summary>
        /// 두 단계 위 디렉터리. 더 올라갈 곳이 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// <c>Path.GetFullPath</c> 는 Windows 에서 <c>/</c> 로 시작하는 경로에 드라이브 문자를 붙이므로 쓰지 않는다.
        /// <c>resolvedPath</c> 는 이미 절대경로라 문자열로 자르면 충분하다.
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
