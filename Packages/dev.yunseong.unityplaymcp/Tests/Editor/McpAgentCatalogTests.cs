using System.IO;
using System.Linq;
using Artel.McpConfig.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests.McpConfig
{
    /// <remarks>
    /// 네 파일이 어디에 있고 어떤 형식인지가 이 기능의 계약 그 자체다. 형식 변환만 검증하면 <c>.cursor</c> 를
    /// <c>.cursors</c> 로 잘못 적어도 전부 green 이다.
    /// </remarks>
    public sealed class McpAgentCatalogTests
    {
        private const string ProjectRoot = "/project";
        private const string HomeDirectory = "/home/someone";

        private static McpAgent Named(string displayName)
        {
            return McpAgent.Catalog(ProjectRoot, HomeDirectory).Single(agent => agent.DisplayName == displayName);
        }

        private static string Expected(params string[] parts)
        {
            return Path.Combine(parts);
        }

        [Test]
        public void CoversTheFourAgents()
        {
            var names = McpAgent.Catalog(ProjectRoot, HomeDirectory).Select(agent => agent.DisplayName).ToArray();

            CollectionAssert.AreEqual(new[] { "Claude Code", "Cursor", "VS Code", "Codex" }, names);
        }

        [Test]
        public void PutsThreeOfThemInTheUnityProjectDirectory()
        {
            Assert.AreEqual(Expected(ProjectRoot, ".mcp.json"), Named("Claude Code").ConfigPath);
            Assert.AreEqual(Expected(ProjectRoot, ".cursor", "mcp.json"), Named("Cursor").ConfigPath);
            Assert.AreEqual(Expected(ProjectRoot, ".vscode", "mcp.json"), Named("VS Code").ConfigPath);
        }

        [Test]
        public void PutsCodexInTheHomeDirectory()
        {
            Assert.AreEqual(Expected(HomeDirectory, ".codex", "config.toml"), Named("Codex").ConfigPath);
        }

        [Test]
        public void GivesClaudeCodeAndCursorTheSameJsonShape()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            foreach (var displayName in new[] { "Claude Code", "Cursor" })
            {
                var written = JObject.Parse(Named(displayName).Format.Add(string.Empty, "unity-play", entry));

                Assert.IsNotNull(written["mcpServers"]["unity-play"], displayName);
                Assert.IsNull(written["mcpServers"]["unity-play"]["type"], displayName);
            }
        }

        [Test]
        public void GivesVisualStudioCodeItsOwnJsonShape()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            var written = JObject.Parse(Named("VS Code").Format.Add(string.Empty, "unity-play", entry));

            Assert.AreEqual("stdio", (string)written["servers"]["unity-play"]["type"]);
        }

        [Test]
        public void GivesCodexTheTomlShape()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            var written = Named("Codex").Format.Add(string.Empty, "unity-play", entry);

            StringAssert.StartsWith("[mcp_servers.unity-play]", written);
        }
    }
}
