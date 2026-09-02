using System.IO;
using UnityPlayMcp.McpConfig.Editor;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.McpConfig
{
    public sealed class McpConfigFileStoreTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "unity-play-mcp-store-" + Path.GetRandomFileName());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void ReadsAMissingFileAsEmptyText()
        {
            Assert.AreEqual(string.Empty, McpConfigFileStore.Read(Path.Combine(_root, "mcp.json")));
        }

        [Test]
        public void CreatesTheDirectoryTheAgentExpects()
        {
            var path = Path.Combine(_root, ".cursor", "mcp.json");

            McpConfigFileStore.Write(path, "{}\n");

            Assert.AreEqual("{}\n", McpConfigFileStore.Read(path));
        }

        [Test]
        public void ReplacesTheWholeFileOnWrite()
        {
            var path = Path.Combine(_root, "mcp.json");

            McpConfigFileStore.Write(path, "first");
            McpConfigFileStore.Write(path, "second");

            Assert.AreEqual("second", McpConfigFileStore.Read(path));
        }
    }
}
