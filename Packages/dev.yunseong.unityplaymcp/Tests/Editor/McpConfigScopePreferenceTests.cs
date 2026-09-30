using UnityPlayMcp.McpConfig.Editor;
using NUnit.Framework;
using UnityEditor;

namespace UnityPlayMcp.Tests.McpConfig
{
    /// <remarks>
    /// <c>EditorPrefs</c> 는 기계의 모든 Unity project 가 공유하므로 key 를 정규화한 project 경로로 나눈다.
    /// 이 test 가 만든 key 는 <see cref="TearDown"/> 에서 지워 개발자의 실제 <c>EditorPrefs</c> 를 남기지 않는다.
    /// </remarks>
    public sealed class McpConfigScopePreferenceTests
    {
        // 실제 project 경로와 겹치지 않는 이름을 쓴다.
        private const string ProjectRootA = "/unity-play-mcp-test-fixture/6c8e2f10-scope-preference-a";
        private const string ProjectRootB = "/unity-play-mcp-test-fixture/6c8e2f10-scope-preference-b";

        [TearDown]
        public void TearDown()
        {
            // KeyFor 가 구분자와 꼬리 slash 를 정규화하므로 이 두 key 만 지우면 된다.
            EditorPrefs.DeleteKey(McpConfigScopePreference.KeyFor(ProjectRootA));
            EditorPrefs.DeleteKey(McpConfigScopePreference.KeyFor(ProjectRootB));
        }

        [Test]
        public void DefaultsToProjectScopeWhenNothingIsStored()
        {
            Assert.AreEqual(McpConfigScope.Project, McpConfigScopePreference.Read(ProjectRootA));
        }

        [Test]
        public void ReadsBackWhatWasWritten()
        {
            McpConfigScopePreference.Write(ProjectRootA, McpConfigScope.User);

            Assert.AreEqual(McpConfigScope.User, McpConfigScopePreference.Read(ProjectRootA));
        }

        [Test]
        public void KeepsTwoProjectsIndependent()
        {
            McpConfigScopePreference.Write(ProjectRootA, McpConfigScope.User);

            Assert.AreEqual(McpConfigScope.Project, McpConfigScopePreference.Read(ProjectRootB));
        }

        [Test]
        public void TreatsSlashBackslashAndTrailingSlashVariantsAsTheSameProject()
        {
            var withTrailingSlash = ProjectRootA + "/";
            var withBackslashes = ProjectRootA.Replace('/', '\\');

            Assert.AreEqual(McpConfigScopePreference.KeyFor(ProjectRootA), McpConfigScopePreference.KeyFor(withTrailingSlash));
            Assert.AreEqual(McpConfigScopePreference.KeyFor(ProjectRootA), McpConfigScopePreference.KeyFor(withBackslashes));

            McpConfigScopePreference.Write(withTrailingSlash, McpConfigScope.User);

            Assert.AreEqual(McpConfigScope.User, McpConfigScopePreference.Read(withBackslashes));
        }

        /// <remarks>
        /// <c>EditorPrefs</c> 에 알 수 없는 값이 남아 있으면 예외 대신 기본값을 써야 화면이 깨지지 않는다.
        /// </remarks>
        [Test]
        public void FallsBackToProjectWhenEditorPrefsHoldsAnUnknownString()
        {
            EditorPrefs.SetString(McpConfigScopePreference.KeyFor(ProjectRootA), "이해할 수 없는 값");

            Assert.AreEqual(McpConfigScope.Project, McpConfigScopePreference.Read(ProjectRootA));
        }
    }
}
