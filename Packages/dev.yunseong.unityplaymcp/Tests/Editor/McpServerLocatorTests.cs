using System;
using System.Collections.Generic;
using Artel.McpConfig.Editor;
using NUnit.Framework;

namespace Artel.Tests.McpConfig
{
    public sealed class McpServerLocatorTests
    {
        private static Func<string, bool> Existing(params string[] paths)
        {
            var present = new HashSet<string>(paths);
            return path => present.Contains(path.Replace('\\', '/'));
        }

        [Test]
        public void FindsTheServerBesideThePackagesDirectory()
        {
            var found = McpServerLocator.FindEntryPoint(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/somewhere/else",
                Existing("/repo/mcp/dist/index.js"));

            Assert.AreEqual("/repo/mcp/dist/index.js", found);
        }

        [Test]
        public void FallsBackToTheProjectRootWhenThePackageIsNotInARepositoryCheckout()
        {
            var found = McpServerLocator.FindEntryPoint(
                null,
                "/repo",
                Existing("/repo/mcp/dist/index.js"));

            Assert.AreEqual("/repo/mcp/dist/index.js", found);
        }

        [Test]
        public void PrefersThePackageSiblingOverTheProjectRoot()
        {
            var found = McpServerLocator.FindEntryPoint(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/project",
                Existing("/repo/mcp/dist/index.js", "/project/mcp/dist/index.js"));

            Assert.AreEqual("/repo/mcp/dist/index.js", found);
        }

        [Test]
        public void FindsNothingWhenTheServerHasNotBeenBuilt()
        {
            var found = McpServerLocator.FindEntryPoint(
                "/repo/Packages/dev.yunseong.unityplaymcp",
                "/repo",
                Existing());

            Assert.IsNull(found);
        }
    }
}
