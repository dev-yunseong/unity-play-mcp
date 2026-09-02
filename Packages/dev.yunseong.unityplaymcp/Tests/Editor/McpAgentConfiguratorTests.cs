using System.IO;
using System.Linq;
using UnityPlayMcp.McpConfig.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.McpConfig
{
    /// <remarks>
    /// <see cref="McpAgentConfigurator"/> 는 <see cref="McpConfigFileStore"/> 로 실제 disk 를 읽고 쓰므로,
    /// mock 이 아니라 <c>Path.GetTempPath()</c> 아래의 진짜 파일로 검증해야 scope 별로 다른 자리에만
    /// 쓰는지, 사람이 적어 둔 내용이 남는지를 믿을 수 있다. <see cref="TearDown"/> 에서 temp 디렉터리를
    /// 통째로 지운다.
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

        /// <summary>사람이 이미 써 둔 설정 파일을 만든다.</summary>
        /// <remarks>
        /// Codex 의 <c>.codex</c> 나 Cursor 의 <c>.cursor</c> 처럼 아직 없는 디렉터리 아래에 있는 자리가 있다.
        /// <see cref="McpConfigFileStore"/> 는 쓸 때 만들어 주지만 test 가 직접 심을 때는 만들어야 한다.
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

            // mtime 이 아니라 내용을 문자열로 그대로 비교한다. 형식 변환을 한 번 거쳤다가 우연히 같은
            // 내용으로 되돌아온 것과, 애초에 손대지 않은 것은 다르다.
            Assert.AreEqual(original, File.ReadAllText(agent.ConfigPath));
        }

        /// <remarks>
        /// 사람이 손으로 망가뜨린 JSON 을 만나면 조용히 덮어쓰지 않고 멈춰야, 그 파일에 남아 있던 다른
        /// 설정이 지워지지 않는다.
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
