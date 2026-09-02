using Artel.McpConfig.Editor;
using NUnit.Framework;

namespace Artel.Tests.McpConfig
{
    public sealed class TomlMcpConfigFormatTests
    {
        private const string ServerName = "unity-play";
        private const string EntryPoint = "/home/someone/dev/unity-play-mcp/mcp/dist/index.js";

        private static McpServerEntry Entry()
        {
            return new McpServerEntry("node", new[] { EntryPoint });
        }

        [Test]
        public void WritesANewFileFromEmptyText()
        {
            var written = new TomlMcpConfigFormat().Add(string.Empty, ServerName, Entry());

            Assert.AreEqual(
                "[mcp_servers.unity-play]\n" +
                "command = \"node\"\n" +
                "args = [\"" + EntryPoint + "\"]\n",
                written);
        }

        [Test]
        public void AppendsBelowTablesItDoesNotOwn()
        {
            const string existing = "approvals_reviewer = \"auto_review\"\n\n[projects.\"/home/someone\"]\ntrust_level = \"trusted\"\n";

            var written = new TomlMcpConfigFormat().Add(existing, ServerName, Entry());

            StringAssert.StartsWith(existing, written);
            StringAssert.Contains("[mcp_servers.unity-play]", written);
        }

        [Test]
        public void ReplacesAnExistingBlockInsteadOfDuplicatingIt()
        {
            var format = new TomlMcpConfigFormat();
            var once = format.Add("[projects.\"/home/someone\"]\ntrust_level = \"trusted\"\n", ServerName, Entry());

            var twice = format.Add(once, ServerName, Entry());

            Assert.AreEqual(once, twice);
            Assert.AreEqual(1, CountOf(twice, "[mcp_servers.unity-play]"));
        }

        [Test]
        public void RemovesTheBlockAndLeavesNeighbouringTables()
        {
            const string existing =
                "[projects.\"/home/someone\"]\n" +
                "trust_level = \"trusted\"\n" +
                "\n" +
                "[mcp_servers.unity-play]\n" +
                "command = \"node\"\n" +
                "args = [\"/old/path/index.js\"]\n" +
                "\n" +
                "[tui]\n" +
                "theme = \"dark\"\n";

            var written = new TomlMcpConfigFormat().Remove(existing, ServerName);

            Assert.AreEqual(
                "[projects.\"/home/someone\"]\n" +
                "trust_level = \"trusted\"\n" +
                "\n" +
                "[tui]\n" +
                "theme = \"dark\"\n",
                written);
        }

        /// <remarks>
        /// Codex 문서가 환경 변수를 이 sub-table 로 적으라고 안내한다. block 을 여기서 끊으면 지운 뒤에
        /// command 없는 env table 만 남아 server 정의가 깨진 채 살아남는다.
        /// </remarks>
        [Test]
        public void RemovesItsOwnSubTableWithTheBlock()
        {
            const string existing =
                "[mcp_servers.unity-play]\n" +
                "command = \"node\"\n" +
                "args = [\"/old/path/index.js\"]\n" +
                "\n" +
                "[mcp_servers.unity-play.env]\n" +
                "MY_ENV_VAR = \"MY_ENV_VALUE\"\n" +
                "\n" +
                "[projects.\"/home/someone\"]\n" +
                "trust_level = \"trusted\"\n";

            var written = new TomlMcpConfigFormat().Remove(existing, ServerName);

            Assert.AreEqual("[projects.\"/home/someone\"]\ntrust_level = \"trusted\"\n", written);
        }

        [Test]
        public void LeavesANeighbourWhoseNameOnlyStartsTheSame()
        {
            const string existing =
                "[mcp_servers.unity-play]\n" +
                "command = \"node\"\n" +
                "\n" +
                "[mcp_servers.unity-play-extra]\n" +
                "command = \"other-server\"\n";

            var written = new TomlMcpConfigFormat().Remove(existing, ServerName);

            Assert.AreEqual("[mcp_servers.unity-play-extra]\ncommand = \"other-server\"\n", written);
        }

        /// <remarks>
        /// 다음 table 바로 위의 주석은 그 table 을 설명하려고 적은 것이다. 우리 block 에 넣고 지우면 남의
        /// 주석이 사라진다.
        /// </remarks>
        [Test]
        public void LeavesACommentThatIntroducesTheNextTable()
        {
            const string existing =
                "[mcp_servers.unity-play]\n" +
                "command = \"node\"\n" +
                "\n" +
                "# Codex 가 믿는 프로젝트들\n" +
                "[projects.\"/home/someone\"]\n" +
                "trust_level = \"trusted\"\n";

            var written = new TomlMcpConfigFormat().Remove(existing, ServerName);

            Assert.AreEqual(
                "# Codex 가 믿는 프로젝트들\n" +
                "[projects.\"/home/someone\"]\n" +
                "trust_level = \"trusted\"\n",
                written);
        }

        /// <remarks>
        /// TOML 은 두 형태를 같은 이름으로 본다. 못 알아보면 table 을 하나 더 붙이게 되고, table 중복 정의는
        /// parse error 라 Codex 가 설정 파일 전체를 읽지 못한다.
        /// </remarks>
        [Test]
        public void RecognisesAQuotedHeader()
        {
            const string existing =
                "[mcp_servers.\"unity-play\"]\n" +
                "command = \"node\"\n" +
                "args = [\"/old/path/index.js\"]\n";

            var format = new TomlMcpConfigFormat();

            Assert.IsTrue(format.Contains(existing, ServerName));
            Assert.AreEqual(1, CountOf(format.Add(existing, ServerName, Entry()), "mcp_servers."));
            Assert.AreEqual(string.Empty, format.Remove(existing, ServerName));
        }

        [Test]
        public void RemovingABlockThatIsNotThereChangesNothing()
        {
            const string existing = "[projects.\"/home/someone\"]\ntrust_level = \"trusted\"\n";

            Assert.AreEqual(existing, new TomlMcpConfigFormat().Remove(existing, ServerName));
        }

        [Test]
        public void ReportsWhetherTheBlockIsAlreadyThere()
        {
            var format = new TomlMcpConfigFormat();

            Assert.IsFalse(format.Contains("[projects.\"/home/someone\"]\ntrust_level = \"trusted\"\n", ServerName));
            Assert.IsTrue(format.Contains(format.Add(string.Empty, ServerName, Entry()), ServerName));
        }

        [Test]
        public void EscapesBackslashesSoAWindowsPathSurvives()
        {
            var entry = new McpServerEntry("node", new[] { @"C:\Users\someone\mcp\dist\index.js" });

            var written = new TomlMcpConfigFormat().Add(string.Empty, ServerName, entry);

            StringAssert.Contains(@"args = [""C:\\Users\\someone\\mcp\\dist\\index.js""]", written);
        }

        [Test]
        public void KeepsTheLineEndingTheFileAlreadyUses()
        {
            const string existing = "[projects.\"/home/someone\"]\r\ntrust_level = \"trusted\"\r\n";

            var written = new TomlMcpConfigFormat().Add(existing, ServerName, Entry());

            StringAssert.Contains("[mcp_servers.unity-play]\r\ncommand = \"node\"\r\n", written);
            Assert.AreEqual(0, CountOf(written.Replace("\r\n", string.Empty), "\n"), "LF 만 남은 줄이 없어야 한다.");
        }

        private static int CountOf(string text, string needle)
        {
            var count = 0;

            for (var index = text.IndexOf(needle, System.StringComparison.Ordinal);
                 index >= 0;
                 index = text.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
