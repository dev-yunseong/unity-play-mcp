using System;
using System.IO;
using System.Linq;
using Artel.McpConfig.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests.McpConfig
{
    /// <remarks>
    /// 네 파일이 어느 scope 에서 어디에 있고 어떤 형식인지가 이 기능의 계약 그 자체다. 형식 변환만
    /// 검증하면 <c>.cursor</c> 를 <c>.cursors</c> 로 잘못 적어도, 또는 <c>User</c> scope 의 자리를 잘못 계산해도
    /// 전부 green 이다.
    /// </remarks>
    public sealed class McpAgentCatalogTests
    {
        private const string ProjectRoot = "/project";
        private const string HomeDirectory = "/home/someone";
        private const string RoamingApplicationDataDirectory = "/roaming/appdata";

        private static McpConfigRoots Roots(
            McpHostPlatform platform = McpHostPlatform.Linux,
            string roamingApplicationDataDirectory = null)
        {
            return new McpConfigRoots(ProjectRoot, HomeDirectory, platform, roamingApplicationDataDirectory);
        }

        private static McpAgent Named(McpConfigScope scope, string displayName, McpConfigRoots roots = null)
        {
            return McpAgent.Catalog(scope, roots ?? Roots()).Single(agent => agent.DisplayName == displayName);
        }

        private static string Expected(params string[] parts)
        {
            return Path.Combine(parts);
        }

        /// <remarks>
        /// <see cref="McpConfigScope"/> 가 <c>internal</c> 이라 <c>public</c> test method 의 인자로 받을 수 없다.
        /// NUnit 의 <c>TestCase</c> 대신 test 안에서 두 scope 를 돌린다.
        /// </remarks>
        private static readonly McpConfigScope[] BothScopes =
        {
            McpConfigScope.Project,
            McpConfigScope.User,
        };

        [Test]
        public void CoversTheFourAgentsInOrder()
        {
            foreach (var scope in BothScopes)
            {
                var names = McpAgent.Catalog(scope, Roots()).Select(agent => agent.DisplayName).ToArray();

                CollectionAssert.AreEqual(new[] { "Claude Code", "Cursor", "VS Code", "Codex" }, names, scope.ToString());
            }
        }

        [Test]
        public void PutsAllFourAgentsInTheUnityProjectDirectoryForProjectScope()
        {
            Assert.AreEqual(Expected(ProjectRoot, ".mcp.json"), Named(McpConfigScope.Project, "Claude Code").ConfigPath);
            Assert.AreEqual(Expected(ProjectRoot, ".cursor", "mcp.json"), Named(McpConfigScope.Project, "Cursor").ConfigPath);
            Assert.AreEqual(Expected(ProjectRoot, ".vscode", "mcp.json"), Named(McpConfigScope.Project, "VS Code").ConfigPath);
            Assert.AreEqual(Expected(ProjectRoot, ".codex", "config.toml"), Named(McpConfigScope.Project, "Codex").ConfigPath);
        }

        [Test]
        public void PutsClaudeCodeCursorAndCodexInTheHomeDirectoryForUserScope()
        {
            // VS Code 의 user 자리는 운영체제마다 다르므로 따로 검증한다.
            Assert.AreEqual(Expected(HomeDirectory, ".claude.json"), Named(McpConfigScope.User, "Claude Code").ConfigPath);
            Assert.AreEqual(Expected(HomeDirectory, ".cursor", "mcp.json"), Named(McpConfigScope.User, "Cursor").ConfigPath);
            Assert.AreEqual(Expected(HomeDirectory, ".codex", "config.toml"), Named(McpConfigScope.User, "Codex").ConfigPath);
        }

        [Test]
        public void PutsVisualStudioCodeUnderRoamingApplicationDataOnWindows()
        {
            var roots = Roots(McpHostPlatform.Windows, RoamingApplicationDataDirectory);

            Assert.AreEqual(
                Expected(RoamingApplicationDataDirectory, "Code", "User", "mcp.json"),
                Named(McpConfigScope.User, "VS Code", roots).ConfigPath);
        }

        [TestCase(null)]
        [TestCase("")]
        public void FallsBackToHomeAppDataRoamingOnWindowsWhenRoamingApplicationDataIsMissing(
            string roamingApplicationDataDirectory)
        {
            var roots = Roots(McpHostPlatform.Windows, roamingApplicationDataDirectory);

            Assert.AreEqual(
                Expected(HomeDirectory, "AppData", "Roaming", "Code", "User", "mcp.json"),
                Named(McpConfigScope.User, "VS Code", roots).ConfigPath);
        }

        [Test]
        public void PutsVisualStudioCodeUnderApplicationSupportOnMacOs()
        {
            var roots = Roots(McpHostPlatform.MacOs);

            Assert.AreEqual(
                Expected(HomeDirectory, "Library", "Application Support", "Code", "User", "mcp.json"),
                Named(McpConfigScope.User, "VS Code", roots).ConfigPath);
        }

        [Test]
        public void PutsVisualStudioCodeUnderDotConfigOnLinux()
        {
            var roots = Roots(McpHostPlatform.Linux);

            Assert.AreEqual(
                Expected(HomeDirectory, ".config", "Code", "User", "mcp.json"),
                Named(McpConfigScope.User, "VS Code", roots).ConfigPath);
        }

        [Test]
        public void GivesClaudeCodeAndCursorTheSameJsonShapeRegardlessOfScope()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            foreach (var scope in BothScopes)
            {
                foreach (var displayName in new[] { "Claude Code", "Cursor" })
                {
                    var written = JObject.Parse(Named(scope, displayName).Format.Add(string.Empty, "unity-play", entry));

                    Assert.IsNotNull(written["mcpServers"]["unity-play"], displayName + " " + scope);
                    Assert.IsNull(written["mcpServers"]["unity-play"]["type"], displayName + " " + scope);
                }
            }
        }

        [Test]
        public void GivesVisualStudioCodeItsOwnJsonShapeRegardlessOfScope()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            foreach (var scope in BothScopes)
            {
                var written = JObject.Parse(Named(scope, "VS Code").Format.Add(string.Empty, "unity-play", entry));

                Assert.AreEqual("stdio", (string)written["servers"]["unity-play"]["type"], scope.ToString());
            }
        }

        [Test]
        public void GivesCodexTheTomlShapeRegardlessOfScope()
        {
            var entry = new McpServerEntry("node", new[] { "/somewhere/index.js" });

            foreach (var scope in BothScopes)
            {
                var written = Named(scope, "Codex").Format.Add(string.Empty, "unity-play", entry);

                StringAssert.StartsWith("[mcp_servers.unity-play]", written);
            }
        }

        [Test]
        public void RefusesANullRootsArgument()
        {
            Assert.Throws<ArgumentNullException>(() => McpAgent.Catalog(McpConfigScope.Project, null));
        }

        [TestCase(null)]
        [TestCase("")]
        public void RefusesAMissingProjectRootWhenBuildingRoots(string projectRoot)
        {
            Assert.Throws<ArgumentException>(
                () => new McpConfigRoots(projectRoot, HomeDirectory, McpHostPlatform.Linux));
        }

        [TestCase(null)]
        [TestCase("")]
        public void RefusesAMissingHomeDirectoryWhenBuildingRoots(string homeDirectory)
        {
            Assert.Throws<ArgumentException>(
                () => new McpConfigRoots(ProjectRoot, homeDirectory, McpHostPlatform.Linux));
        }
    }
}
