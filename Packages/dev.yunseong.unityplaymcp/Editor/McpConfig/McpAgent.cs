using System;
using System.Collections.Generic;
using System.IO;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정을 쓸 수 있는 coding agent 의 표시 이름, 설정 파일 경로, 파일 형식.
    /// </summary>
    internal sealed class McpAgent
    {
        internal McpAgent(string displayName, string configPath, IMcpConfigFormat format)
        {
            DisplayName = displayName;
            ConfigPath = configPath;
            Format = format;
        }

        internal string DisplayName { get; }

        internal string ConfigPath { get; }

        internal IMcpConfigFormat Format { get; }

        /// <summary>
        /// 지원하는 agent 목록을 scope 기준으로 만든다.
        /// </summary>
        /// <remarks>
        /// agent 별 경로와 형식을 한 곳에 모은다. 형식은 scope 와 무관하다.
        /// </remarks>
        internal static IReadOnlyList<McpAgent> Catalog(McpConfigScope scope, McpConfigRoots roots)
        {
            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }

            return new[]
            {
                new McpAgent(
                    "Claude Code",
                    ClaudeCodePath(scope, roots),
                    new JsonMcpConfigFormat("mcpServers", writesTransportType: false)),
                new McpAgent(
                    "Cursor",
                    CursorPath(scope, roots),
                    new JsonMcpConfigFormat("mcpServers", writesTransportType: false)),
                new McpAgent(
                    "VS Code",
                    VisualStudioCodePath(scope, roots),
                    new JsonMcpConfigFormat("servers", writesTransportType: true)),
                new McpAgent(
                    "Codex",
                    CodexPath(scope, roots),
                    new TomlMcpConfigFormat()),
            };
        }

        /// <summary>user scope 는 파일 이름이 다르다. 홈에는 <c>.mcp.json</c> 대신 <c>.claude.json</c> 을 쓴다.</summary>
        private static string ClaudeCodePath(McpConfigScope scope, McpConfigRoots roots)
        {
            return scope == McpConfigScope.User
                ? Path.Combine(roots.HomeDirectory, ".claude.json")
                : Path.Combine(roots.ProjectRoot, ".mcp.json");
        }

        private static string CursorPath(McpConfigScope scope, McpConfigRoots roots)
        {
            return Path.Combine(RootFor(scope, roots), ".cursor", "mcp.json");
        }

        /// <summary>
        /// Visual Studio Code 의 user 설정만 운영체제마다 경로가 다르다.
        /// </summary>
        /// <remarks>
        /// Windows 는 <c>%APPDATA%</c>, macOS 는 <c>~/Library/Application Support</c>, Linux 는 <c>~/.config</c>
        /// 아래다. Mono 의 <c>SpecialFolder.ApplicationData</c> 는 macOS 에서도 <c>~/.config</c> 를 돌려주므로 쓰지 않는다.
        /// </remarks>
        private static string VisualStudioCodePath(McpConfigScope scope, McpConfigRoots roots)
        {
            if (scope != McpConfigScope.User)
            {
                return Path.Combine(roots.ProjectRoot, ".vscode", "mcp.json");
            }

            return Path.Combine(VisualStudioCodeUserRoot(roots), "Code", "User", "mcp.json");
        }

        private static string VisualStudioCodeUserRoot(McpConfigRoots roots)
        {
            switch (roots.Platform)
            {
                case McpHostPlatform.Windows:
                    // %APPDATA% 를 받지 못하면 Windows 기본 경로를 쓴다.
                    return string.IsNullOrEmpty(roots.RoamingApplicationDataDirectory)
                        ? Path.Combine(roots.HomeDirectory, "AppData", "Roaming")
                        : roots.RoamingApplicationDataDirectory;
                case McpHostPlatform.MacOs:
                    return Path.Combine(roots.HomeDirectory, "Library", "Application Support");
                default:
                    return Path.Combine(roots.HomeDirectory, ".config");
            }
        }

        private static string CodexPath(McpConfigScope scope, McpConfigRoots roots)
        {
            return Path.Combine(RootFor(scope, roots), ".codex", "config.toml");
        }

        private static string RootFor(McpConfigScope scope, McpConfigRoots roots)
        {
            return scope == McpConfigScope.User ? roots.HomeDirectory : roots.ProjectRoot;
        }
    }
}
