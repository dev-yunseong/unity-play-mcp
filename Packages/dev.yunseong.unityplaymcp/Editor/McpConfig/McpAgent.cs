using System;
using System.Collections.Generic;
using System.IO;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정을 써 넣을 수 있는 coding agent 한 곳: 화면에 보일 이름, 설정 파일 자리, 그 파일의 형식.
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
        /// 지원하는 네 곳을 고른 scope 기준으로 만든다.
        /// </summary>
        /// <remarks>
        /// agent 마다 자리와 형식이 다르고, 같은 agent 도 scope 에 따라 자리가 다르다. 이 세 가지가 한 곳에
        /// 모여 있어야 자리를 고칠 때 볼 곳이 하나다. 형식은 scope 와 무관하므로 scope 로 갈리지 않는다.
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

        /// <summary>user scope 는 파일 이름부터 다르다. project 의 <c>.mcp.json</c> 이 홈에는 없다.</summary>
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
        /// Visual Studio Code 의 user 설정만 운영체제마다 자리가 다르다.
        /// </summary>
        /// <remarks>
        /// Windows 는 <c>%APPDATA%</c>, macOS 는 <c>~/Library/Application Support</c>, Linux 는 <c>~/.config</c>
        /// 아래다. Mono 의 <c>SpecialFolder.ApplicationData</c> 는 macOS 에서도 <c>~/.config</c> 를 돌려주므로
        /// 그 값 하나로 세 갈래를 대신할 수 없다.
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
                    // %APPDATA% 를 못 받았을 때의 기본 자리. 홈 아래에 바로 만들어 엉뚱한 곳을 쓰지 않는다.
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
