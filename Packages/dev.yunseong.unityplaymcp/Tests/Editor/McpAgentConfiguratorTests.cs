using System.IO;
using System.Linq;
using UnityPlayMcp.McpConfig.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.McpConfig
{
    /// <remarks>
    /// <see cref="McpAgentConfigurator"/> 는 <see cref="McpConfigFileStore"/> 로 실제 disk 에 쓰므로
    /// <c>Path.GetTempPath()</c> 아래 실제 파일로 검증한다. <see cref="TearDown"/> 에서 temp 디렉터리를 지운다.
    /// </remarks>
    public sealed class McpAgentConfiguratorTests
    {
        private const string ServerName = "unity-play";

        private string _root;
        private McpConfigRoots _roots;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "unity-play-mcp-configurator-" + Path.GetRandomFileName());

            var projectRoot = Path.Combine(_root, "project");
            var homeDirectory = Path.Combine(_root, "home");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(homeDirectory);

            _roots = new McpConfigRoots(projectRoot, homeDirectory, McpHostPlatform.Linux);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private McpAgent Named(McpConfigScope scope, string displayName)
        {
            return McpAgent.Catalog(scope, _roots).Single(agent => agent.DisplayName == displayName);
        }

        private static McpServerEntry Entry()
        {
            return new McpServerEntry("node", new[] { "/somewhere/index.js" });
        }

        /// <summary>사용자가 이미 써 둔 설정 파일을 만든다.</summary>
        /// <remarks>
        /// <c>.codex</c>, <c>.cursor</c> 처럼 아직 없는 디렉터리가 있으므로 test 가 직접 만든다.
        /// </remarks>
        private static void Seed(McpAgent agent, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(agent.ConfigPath));
            File.WriteAllText(agent.ConfigPath, text);
        }

        [Test]
        public void ReportsConfiguredAfterAdd()
        {
            var agent = Named(McpConfigScope.Project, "Claude Code");

            McpAgentConfigurator.Add(agent, ServerName, Entry());

            Assert.IsTrue(McpAgentConfigurator.IsConfigured(agent, ServerName));
        }

        [Test]
        public void AddOnlyCreatesTheFileForTheChosenScope()
        {
            var projectAgent = Named(McpConfigScope.Project, "Claude Code");
            var userAgent = Named(McpConfigScope.User, "Claude Code");

            McpAgentConfigurator.Add(projectAgent, ServerName, Entry());

            Assert.IsTrue(File.Exists(projectAgent.ConfigPath));
            Assert.IsFalse(File.Exists(userAgent.ConfigPath));
        }

        [Test]
        public void KeepsOtherJsonServerEntriesAndHandWrittenKeys()
        {
            var agent = Named(McpConfigScope.Project, "Claude Code");
            const string original = @"{
  ""$schema"": ""https://example.test/schema.json"",
  ""mcpServers"": {
    ""other"": { ""command"": ""other-server"" }
  }
}";
            Seed(agent, original);

            McpAgentConfigurator.Add(agent, ServerName, Entry());
            McpAgentConfigurator.Remove(agent, ServerName);

            var root = JObject.Parse(File.ReadAllText(agent.ConfigPath));
            Assert.AreEqual("https://example.test/schema.json", (string)root["$schema"]);
            Assert.AreEqual("other-server", (string)root["mcpServers"]["other"]["command"]);
            Assert.IsNull(root["mcpServers"][ServerName]);
        }

        [Test]
        public void KeepsOtherTomlTablesAndHandWrittenKeys()
        {
            var agent = Named(McpConfigScope.Project, "Codex");
            const string original =
                "[profile.default]\n" +
                "model = \"gpt-5\"\n" +
                "\n" +
                "[mcp_servers.other]\n" +
                "command = \"other-server\"\n" +
                "args = []\n";
            Seed(agent, original);

            McpAgentConfigurator.Add(agent, ServerName, Entry());
            McpAgentConfigurator.Remove(agent, ServerName);

            var text = File.ReadAllText(agent.ConfigPath);
            StringAssert.Contains("[profile.default]", text);
            StringAssert.Contains("model = \"gpt-5\"", text);
            StringAssert.Contains("[mcp_servers.other]", text);
            StringAssert.Contains("command = \"other-server\"", text);
            StringAssert.DoesNotContain("[mcp_servers.unity-play]", text);
        }

        [Test]
        public void RemoveDoesNotCreateAFileWhenTheServerWasNeverThere()
        {
            var agent = Named(McpConfigScope.Project, "Claude Code");

            McpAgentConfigurator.Remove(agent, ServerName);

            Assert.IsFalse(File.Exists(agent.ConfigPath));
        }

        [Test]
        public void RemoveLeavesAnExistingFileUnchangedWhenTheServerIsNotThere()
        {
            var agent = Named(McpConfigScope.Project, "Claude Code");
            const string original = "{\n  \"mcpServers\": {\n    \"other\": { \"command\": \"other-server\" }\n  }\n}\n";
            Seed(agent, original);

            McpAgentConfigurator.Remove(agent, ServerName);

            // 파일이 다시 쓰이지 않았는지 내용 문자열로 비교한다.
            Assert.AreEqual(original, File.ReadAllText(agent.ConfigPath));
        }

        /// <remarks>
        /// 손상된 JSON 은 덮어쓰지 않고 멈춰야 그 파일의 다른 설정이 지워지지 않는다.
        /// </remarks>
        [Test]
        public void ThrowsOnAMalformedFileAndLeavesItUntouched()
        {
            var agent = Named(McpConfigScope.Project, "Claude Code");
            const string malformed = "{ this is not json";
            Seed(agent, malformed);

            Assert.Throws<JsonReaderException>(() => McpAgentConfigurator.IsConfigured(agent, ServerName));
            Assert.Throws<JsonReaderException>(() => McpAgentConfigurator.Add(agent, ServerName, Entry()));
            Assert.AreEqual(malformed, File.ReadAllText(agent.ConfigPath));
        }

        [Test]
        public void RemovingFromOneScopeDoesNotTouchTheOtherScopesFile()
        {
            var projectAgent = Named(McpConfigScope.Project, "Codex");
            var userAgent = Named(McpConfigScope.User, "Codex");

            McpAgentConfigurator.Add(projectAgent, ServerName, Entry());
            McpAgentConfigurator.Add(userAgent, ServerName, Entry());

            McpAgentConfigurator.Remove(projectAgent, ServerName);

            Assert.IsFalse(McpAgentConfigurator.IsConfigured(projectAgent, ServerName));
            Assert.IsTrue(McpAgentConfigurator.IsConfigured(userAgent, ServerName));
        }
    }
}
