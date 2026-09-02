using System;
using System.Collections.Generic;
using UnityPlayMcp.McpConfig.Editor;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.McpConfig
{
    public sealed class McpServerLocatorTests
    {
        private static Func<string, bool> Existing(params string[] paths)
        {
            var present = new HashSet<string>(paths);
            return path => present.Contains(path.Replace('\\', '/'));
        }

        [Test]
        public void UsesTheLocalBuildBesideThePackagesDirectory()
        {
            var found = McpServerLocator.Resolve(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/somewhere/else",
                "0.1.0",
                Existing("/repo/mcp/dist/index.js"));

            Assert.AreEqual("node", found.Command);
            CollectionAssert.AreEqual(new[] { "/repo/mcp/dist/index.js" }, found.Arguments);
        }

        [Test]
        public void UsesTheLocalBuildAtTheProjectRootWhenThePackageIsNotInARepositoryCheckout()
        {
            var found = McpServerLocator.Resolve(
                null,
                "/repo",
                "0.1.0",
                Existing("/repo/mcp/dist/index.js"));

            Assert.AreEqual("node", found.Command);
            CollectionAssert.AreEqual(new[] { "/repo/mcp/dist/index.js" }, found.Arguments);
        }

        [Test]
        public void PrefersThePackageSiblingOverTheProjectRoot()
        {
            var found = McpServerLocator.Resolve(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/project",
                "0.1.0",
                Existing("/repo/mcp/dist/index.js", "/project/mcp/dist/index.js"));

            Assert.AreEqual("node", found.Command);
            CollectionAssert.AreEqual(new[] { "/repo/mcp/dist/index.js" }, found.Arguments);
        }

        [Test]
        public void UsesPinnedNpxWhenNoLocalBuildExists()
        {
            var found = McpServerLocator.Resolve(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/repo",
                "0.1.0",
                Existing());

            Assert.AreEqual("npx", found.Command);
            CollectionAssert.AreEqual(new[] { "-y", "unity-play-mcp@0.1.0" }, found.Arguments);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void RefusesNpxWhenTheMcpServerVersionIsMissingOrEmpty(string mcpServerVersion)
        {
            var found = McpServerLocator.Resolve(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/repo",
                mcpServerVersion,
                Existing());

            Assert.IsNull(found);
        }
    }
}
