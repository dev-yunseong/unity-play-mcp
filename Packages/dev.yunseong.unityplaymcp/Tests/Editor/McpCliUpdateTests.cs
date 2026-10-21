using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityPlayMcp.McpConfig.Editor;

namespace UnityPlayMcp.Tests.McpConfig
{
    public sealed class McpCliUpdateTests
    {
        private const string Name = "unity-play";
        private const string Json = "{\"mcpServers\":{\"unity-play\":{\"command\":\"npx\",\"args\":[\"-y\",\"unity-play-mcp@0.3.0\"],\"env\":{\"TOKEN\":\"keep\"},\"timeout\":42},\"other\":{\"command\":\"keep\"}}}";
        private const string Toml = "# keep header\n[mcp_servers.unity-play]\ncommand = \"npx\"\nargs = [\"-y\", \"unity-play-mcp@0.3.0\"] # keep pin comment\nstartup_timeout_sec = 42\n[mcp_servers.unity-play.env]\nTOKEN = \"keep\"\n\n[mcp_servers.other]\ncommand = \"keep\"\n";

        private static IMcpConfigFormat Format(bool toml) =>
            toml ? (IMcpConfigFormat)new TomlMcpConfigFormat() : new JsonMcpConfigFormat("mcpServers", false);

        [TestCase(false)]
        [TestCase(true)]
        public void UpdatesOnlyThePinAndPreservesServerOptions(bool toml)
        {
            var format = Format(toml);
            var original = toml ? Toml : Json;
            var updated = format.UpdateArguments(original, Name, McpCliVersion.Pinned("0.4.0").Arguments);
            Assert.AreEqual("0.4.0", McpCliVersion.ConfiguredVersion(format.ReadEntry(updated, Name)));
            if (toml)
                Assert.AreEqual(original.Replace("unity-play-mcp@0.3.0", "unity-play-mcp@0.4.0"), updated);
            else
            {
                var before = JObject.Parse(original);
                before["mcpServers"][Name]["args"][1] = "unity-play-mcp@0.4.0";
                Assert.IsTrue(JToken.DeepEquals(before, JObject.Parse(updated)));
            }
            Assert.AreEqual(updated, format.UpdateArguments(updated, Name, McpCliVersion.Pinned("0.4.0").Arguments));
        }

        [Test]
        public void OnlyTheGeneratedExactNpxShapeIsAutomaticallyEditable()
        {
            foreach (var entry in new[]
            {
                new McpServerEntry("npx.cmd", new[] { "-y", "unity-play-mcp@0.3.0" }),
                new McpServerEntry("node", new[] { "dist/index.js" }),
                new McpServerEntry("wrapper", new[] { "npx", "-y", "unity-play-mcp@0.3.0" }),
                new McpServerEntry("npx", new[] { "-y", "unity-play-mcp@0.3.0", "--custom" }),
                new McpServerEntry("npx", new[] { "-y", "unity-play-mcp@latest" }),
                new McpServerEntry("npx", new[] { "-y", "unity-play-mcp@^0.3.0" }),
                new McpServerEntry("npx", new[] { "-y", "unity-play-mcp" }),
                new McpServerEntry("npx", new[] { "-y", "other-unity-play-mcp@0.3.0" }),
            })
                Assert.IsNull(McpCliVersion.ConfiguredVersion(entry));
            Assert.AreEqual("0.4.0-rc.1", McpCliVersion.ConfiguredVersion(McpCliVersion.Pinned("0.4.0-rc.1")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfiguratorRechecksCurrentPinAndDoesNotOverwriteCustomEdits(bool toml)
        {
            var path = Path.Combine(Path.GetTempPath(), "unity-play-update-" + Guid.NewGuid() + (toml ? ".toml" : ".json"));
            try
            {
                var original = toml ? Toml : Json;
                File.WriteAllText(path, original);
                var agent = new McpAgent("test", path, Format(toml));
                Assert.Throws<InvalidOperationException>(() => McpAgentConfigurator.UpdateCli(agent, Name, "0.2.0", "0.4.0"));
                Assert.AreEqual(original, File.ReadAllText(path));
                McpAgentConfigurator.UpdateCli(agent, Name, "0.3.0", "0.4.0");
                Assert.AreEqual("0.4.0", McpCliVersion.ConfiguredVersion(McpAgentConfigurator.ReadEntry(agent, Name)));
                var updated = File.ReadAllText(path);
                McpAgentConfigurator.UpdateCli(agent, Name, "0.4.0", "0.4.0");
                Assert.AreEqual(updated, File.ReadAllText(path));
                File.WriteAllText(path, original.Replace("npx", "custom-wrapper"));
                Assert.Throws<InvalidOperationException>(() => McpAgentConfigurator.UpdateCli(agent, Name, "0.3.0", "0.4.0"));
                Assert.AreEqual(original.Replace("npx", "custom-wrapper"), File.ReadAllText(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void JsonCommentsAndDuplicatePropertiesAreNotRewritten()
        {
            var format = Format(false);
            var args = McpCliVersion.Pinned("0.4.0").Arguments;
            Assert.Throws<InvalidOperationException>(() => format.UpdateArguments("// comment\n" + Json, Name, args));
            Assert.Throws<JsonReaderException>(() => format.UpdateArguments(Json.Replace("\"timeout\":42", "\"timeout\":42,\"timeout\":43"), Name, args));
        }

        [Test]
        public void TomlAmbiguousLaunchesRequireManualUpdate()
        {
            var format = Format(true);
            foreach (var invalid in new[]
            {
                Toml + "\n[mcp_servers.unity-play]\ncommand = \"npx\"\nargs = []\n",
                Toml.Replace("command = \"npx\"", "command = \"npx\"\ncommand = \"npx\""),
                Toml.Replace("args = [", "args = [\n"),
                Toml.Replace("command = \"npx\"", "command = 'npx'"),
                Toml.Replace("command = \"npx\"", "command = \"npx\"\n\"command\" = \"custom\""),
                Toml.Replace("startup_timeout_sec = 42", "description = \"\"\"\nargs = []\n\"\"\""),
                "instructions = '''\n[mcp_servers.unity-play]\ncommand = \"npx\"\nargs = [\"-y\", \"unity-play-mcp@0.3.0\"]\n[example]\n'''\n",
                "instructions = \"\"\"\n[mcp_servers.unity-play]\ncommand = \"npx\"\nargs = [\"-y\", \"unity-play-mcp@0.3.0\"]\n[example]\n\"\"\"\n",
            })
                Assert.Throws<InvalidOperationException>(() => format.UpdateArguments(invalid, Name, McpCliVersion.Pinned("0.4.0").Arguments));
        }

        [Test]
        public void TomlUpdatePreservesCrLfAndInlineComments()
        {
            var format = Format(true);
            var original = Toml.Replace("\n", "\r\n");
            Assert.AreEqual(original.Replace("@0.3.0", "@0.4.0"),
                format.UpdateArguments(original, Name, McpCliVersion.Pinned("0.4.0").Arguments));
        }
    }
}
