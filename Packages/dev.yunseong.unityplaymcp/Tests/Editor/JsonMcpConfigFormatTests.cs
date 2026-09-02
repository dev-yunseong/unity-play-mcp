using System;
using Artel.McpConfig.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests.McpConfig
{
    public sealed class JsonMcpConfigFormatTests
    {
        private const string ServerName = "unity-play";
        private const string EntryPoint = "/home/someone/dev/unity-play-mcp/mcp/dist/index.js";

        private static McpServerEntry Entry()
        {
            return new McpServerEntry("node", new[] { EntryPoint });
        }

        private static JsonMcpConfigFormat ClaudeCode()
        {
            return new JsonMcpConfigFormat("mcpServers", writesTransportType: false);
        }

        private static JsonMcpConfigFormat VisualStudioCode()
        {
            return new JsonMcpConfigFormat("servers", writesTransportType: true);
        }

        [Test]
        public void WritesANewFileFromEmptyText()
        {
            var written = ClaudeCode().Add(string.Empty, ServerName, Entry());

            var server = JObject.Parse(written)["mcpServers"][ServerName];
            Assert.AreEqual("node", (string)server["command"]);
            Assert.AreEqual(EntryPoint, (string)server["args"][0]);
            Assert.IsNull(server["type"], "type 은 VS Code 형식에만 붙는다.");
        }

        [Test]
        public void KeepsOtherServers()
        {
            const string existing = @"{
  ""mcpServers"": {
    ""other"": { ""command"": ""other-server"" }
  }
}";

            var written = ClaudeCode().Add(existing, ServerName, Entry());

            var servers = JObject.Parse(written)["mcpServers"];
            Assert.AreEqual("other-server", (string)servers["other"]["command"]);
            Assert.AreEqual("node", (string)servers[ServerName]["command"]);
        }

        [Test]
        public void KeepsEverythingOutsideTheServerList()
        {
            const string existing = @"{
  ""$schema"": ""https://example.test/schema.json"",
  ""mcpServers"": {}
}";

            var written = ClaudeCode().Add(existing, ServerName, Entry());

            Assert.AreEqual("https://example.test/schema.json", (string)JObject.Parse(written)["$schema"]);
        }

        [Test]
        public void ReplacesAnExistingEntryInsteadOfDuplicatingIt()
        {
            const string existing = @"{
  ""mcpServers"": {
    ""unity-play"": { ""command"": ""node"", ""args"": [""/old/path/index.js""] }
  }
}";

            var written = ClaudeCode().Add(existing, ServerName, Entry());

            var servers = (JObject)JObject.Parse(written)["mcpServers"];
            Assert.AreEqual(1, servers.Count);
            Assert.AreEqual(EntryPoint, (string)servers[ServerName]["args"][0]);
        }

        [Test]
        public void RemovesOnlyTheNamedServer()
        {
            var withBoth = ClaudeCode().Add(@"{ ""mcpServers"": { ""other"": { ""command"": ""other-server"" } } }",
                ServerName, Entry());

            var written = ClaudeCode().Remove(withBoth, ServerName);

            var servers = (JObject)JObject.Parse(written)["mcpServers"];
            Assert.AreEqual(1, servers.Count);
            Assert.IsNotNull(servers["other"]);
        }

        [Test]
        public void RemovingAServerThatIsNotThereChangesNoServers()
        {
            var written = ClaudeCode().Remove(@"{ ""mcpServers"": { ""other"": { ""command"": ""other-server"" } } }",
                ServerName);

            var servers = (JObject)JObject.Parse(written)["mcpServers"];
            Assert.AreEqual(1, servers.Count);
            Assert.IsNotNull(servers["other"]);
        }

        [Test]
        public void ReportsWhetherTheServerIsAlreadyThere()
        {
            var format = ClaudeCode();

            Assert.IsFalse(format.Contains(string.Empty, ServerName));
            Assert.IsTrue(format.Contains(format.Add(string.Empty, ServerName, Entry()), ServerName));
        }

        [Test]
        public void VisualStudioCodeUsesItsOwnRootKeyAndTransportType()
        {
            var written = VisualStudioCode().Add(string.Empty, ServerName, Entry());

            var root = JObject.Parse(written);
            Assert.IsNull(root["mcpServers"]);
            Assert.AreEqual("stdio", (string)root["servers"][ServerName]["type"]);
            Assert.AreEqual("node", (string)root["servers"][ServerName]["command"]);
        }

        /// <remarks>
        /// Newtonsoft 의 JObject 는 object 안의 주석을 담지 못한다. 읽어서 다시 쓰면 사용자가 적어 둔 주석이
        /// 사라지므로, 조용히 지우는 대신 멈춘다.
        /// </remarks>
        [Test]
        public void RefusesToRewriteAFileWithComments()
        {
            const string existing = @"{
  // 이 줄은 사람이 적어 둔 것이다.
  ""servers"": {}
}";

            Assert.Throws<InvalidOperationException>(
                () => VisualStudioCode().Add(existing, ServerName, Entry()));
            Assert.Throws<InvalidOperationException>(
                () => VisualStudioCode().Remove(existing, ServerName));
        }

        [Test]
        public void StillReportsStatusForAFileWithComments()
        {
            const string existing = @"{
  // 이 줄은 사람이 적어 둔 것이다.
  ""servers"": { ""unity-play"": { ""command"": ""node"" } }
}";

            Assert.IsTrue(VisualStudioCode().Contains(existing, ServerName));
        }

        [Test]
        public void RefusesAFileWhoseServerListIsNotAnObject()
        {
            Assert.Throws<InvalidOperationException>(
                () => ClaudeCode().Add(@"{ ""mcpServers"": ""not an object"" }", ServerName, Entry()));
        }

        /// <remarks>
        /// JObject.ToString 은 Environment.NewLine 으로 줄을 바꾼다. 개행을 파일에 맞추지 않으면 Windows 에서
        /// 본문만 CRLF 이고 마지막 줄은 LF 인 파일이 나온다.
        /// </remarks>
        [Test]
        public void KeepsTheLineEndingTheFileAlreadyUses()
        {
            var written = ClaudeCode().Add("{\r\n  \"mcpServers\": {}\r\n}\r\n", ServerName, Entry());

            Assert.IsFalse(written.Replace("\r\n", string.Empty).Contains("\n"), "LF 만 남은 줄이 없어야 한다.");
            StringAssert.EndsWith("\r\n", written);
        }

        [Test]
        public void WritesLineFeedsIntoAFileThatUsesThem()
        {
            var written = ClaudeCode().Add("{\n  \"mcpServers\": {}\n}\n", ServerName, Entry());

            Assert.IsFalse(written.Contains("\r"), "CR 이 섞이지 않아야 한다.");
        }

        [Test]
        public void RefusesTextItCannotRead()
        {
            Assert.Throws<JsonReaderException>(() => ClaudeCode().Add("{ this is not json", ServerName, Entry()));
        }
    }
}
