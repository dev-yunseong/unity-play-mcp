using Artel.McpConfig.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Artel.Tests.McpConfig
{
    /// <remarks>
    /// <c>EditorPrefs</c> 는 한 기계의 모든 Unity project 가 함께 쓴다. project 경로로 key 를 가르지 않으면
    /// 한 project 에서 고른 scope 가 다른 project 화면에도 새어 나가고, 경로 표기만 다른 같은 project 를
    /// 서로 다른 project 로 잘못 셀 수 있다. 이 test 가 만드는 key 는 <see cref="TearDown"/> 에서 반드시
    /// 지워, 이 test 를 돌리는 개발자의 실제 <c>EditorPrefs</c> 를 더럽히지 않는다.
    /// </remarks>
    public sealed class McpConfigScopePreferenceTests
    {
        // 실제 개발자 기계의 project 경로와 겹치지 않도록, 있을 법하지 않은 이름을 쓴다.
        private const string ProjectRootA = "/unity-play-mcp-test-fixture/6c8e2f10-scope-preference-a";
        private const string ProjectRootB = "/unity-play-mcp-test-fixture/6c8e2f10-scope-preference-b";

        [TearDown]
        public void TearDown()
        {
            // KeyFor 가 구분자와 꼬리 slash 를 정규화하므로, 이 두 key 만 지워도 이 test 가 심은 값은 전부 없어진다.
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
        /// enum 순서를 바꾸거나 다른 기능이 같은 key 를 건드리면 <c>EditorPrefs</c> 에 알 수 없는 문자열이
        /// 남을 수 있다. 그럴 때 예외를 던지는 대신 기본값으로 떨어져야 화면이 깨지지 않는다.
        /// </remarks>
        [Test]
        public void FallsBackToProjectWhenEditorPrefsHoldsAnUnknownString()
        {
            EditorPrefs.SetString(McpConfigScopePreference.KeyFor(ProjectRootA), "이해할 수 없는 값");

            Assert.AreEqual(McpConfigScope.Project, McpConfigScopePreference.Read(ProjectRootA));
        }
    }
}
