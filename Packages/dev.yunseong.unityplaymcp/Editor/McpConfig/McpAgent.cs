using System.Collections.Generic;
using System.IO;

namespace Artel.McpConfig.Editor
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
        /// 지원하는 네 곳을 만든다.
        /// </summary>
        /// <remarks>
        /// Codex 만 홈 디렉터리에 하나뿐인 설정을 쓰고, 나머지 셋은 Unity project 안에 각자의 자리를 갖는다.
        /// 두 뿌리 경로는 호출하는 쪽이 한 번 구해서 넘긴다.
        /// </remarks>
        internal static IReadOnlyList<McpAgent> Catalog(string projectRoot, string homeDirectory)
        {
            return new[]
            {
                new McpAgent(
                    "Claude Code",
                    Path.Combine(projectRoot, ".mcp.json"),
                    new JsonMcpConfigFormat("mcpServers", writesTransportType: false)),
                new McpAgent(
                    "Cursor",
                    Path.Combine(projectRoot, ".cursor", "mcp.json"),
                    new JsonMcpConfigFormat("mcpServers", writesTransportType: false)),
                new McpAgent(
                    "VS Code",
                    Path.Combine(projectRoot, ".vscode", "mcp.json"),
                    new JsonMcpConfigFormat("servers", writesTransportType: true)),
                new McpAgent(
                    "Codex",
                    Path.Combine(homeDirectory, ".codex", "config.toml"),
                    new TomlMcpConfigFormat()),
            };
        }
    }
}
