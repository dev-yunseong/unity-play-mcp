using UnityPlayMcp.Affordances.Live;
using NUnit.Framework;
using UnityEditor;

namespace UnityPlayMcp.Tests.McpConfig
{
    /// <remarks>
    /// <c>EditorPrefs</c> 는 기계의 모든 Unity project 가 공유하므로 key 를 정규화한 project 경로로 나눈다.
    /// 이 test 가 만든 key 는 <see cref="TearDown"/> 에서 지워 개발자의 실제 <c>EditorPrefs</c> 를 남기지 않는다.
    /// </remarks>
    public sealed class PulseIntervalPreferenceTests
    {
            // 실제 project 경로와 겹치지 않는 이름을 쓴다.
        private const string ProjectRootA = "/unity-play-mcp-test-fixture/3f51a9d7-pulse-interval-a";
        private const string ProjectRootB = "/unity-play-mcp-test-fixture/3f51a9d7-pulse-interval-b";

        [TearDown]
        public void TearDown()
        {
            // KeyFor 가 구분자와 꼬리 slash 를 정규화하므로 이 두 key 만 지우면 된다.
            EditorPrefs.DeleteKey(PulseIntervalPreference.KeyFor(ProjectRootA));
            EditorPrefs.DeleteKey(PulseIntervalPreference.KeyFor(ProjectRootB));
        }

        [Test]
        public void DefaultsToOneSecondWhenNothingIsStored()
        {
            Assert.AreEqual(1f, PulseIntervalPreference.Default);
            Assert.AreEqual(1f, PulseIntervalPreference.Read(ProjectRootA));
        }

        [Test]
        public void ReadsBackWhatWasWritten()
        {
            PulseIntervalPreference.Write(ProjectRootA, 0.25f);

            Assert.AreEqual(0.25f, PulseIntervalPreference.Read(ProjectRootA));
        }

        [Test]
        public void ReturnsTheValueItStored()
        {
            Assert.AreEqual(0.25f, PulseIntervalPreference.Write(ProjectRootA, 0.25f));
            Assert.AreEqual(PulseIntervalPreference.Maximum, PulseIntervalPreference.Write(ProjectRootA, 60f));
        }

        [Test]
        public void KeepsTwoProjectsIndependent()
        {
            PulseIntervalPreference.Write(ProjectRootA, 0.25f);

            Assert.AreEqual(PulseIntervalPreference.Default, PulseIntervalPreference.Read(ProjectRootB));
        }

        [Test]
        public void TreatsSlashBackslashAndTrailingSlashVariantsAsTheSameProject()
        {
            var withTrailingSlash = ProjectRootA + "/";
            var withBackslashes = ProjectRootA.Replace('/', '\\');

            Assert.AreEqual(PulseIntervalPreference.KeyFor(ProjectRootA), PulseIntervalPreference.KeyFor(withTrailingSlash));
            Assert.AreEqual(PulseIntervalPreference.KeyFor(ProjectRootA), PulseIntervalPreference.KeyFor(withBackslashes));

            PulseIntervalPreference.Write(withTrailingSlash, 0.25f);

            Assert.AreEqual(0.25f, PulseIntervalPreference.Read(withBackslashes));
        }

        /// <remarks>
        /// <c>EditorPrefs</c> 에 숫자가 아닌 값이 남아 있으면 예외 대신 기본값을 써야 화면과 channel 이 동작한다.
        /// </remarks>
        [Test]
        public void FallsBackToTheDefaultWhenEditorPrefsHoldsSomethingThatIsNotANumber()
        {
            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "이해할 수 없는 값");

            Assert.AreEqual(PulseIntervalPreference.Default, PulseIntervalPreference.Read(ProjectRootA));
        }

        /// <remarks>
        /// NaN 과 무한대는 비교가 모두 false 라 clamp 를 통과하고, <c>Pulse.Begin</c> 에 닿으면 channel 이 시작하지 않는다.
        /// </remarks>
        [Test]
        public void FallsBackToTheDefaultWhenTheStoredNumberIsNotFinite()
        {
            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "NaN");
            Assert.AreEqual(PulseIntervalPreference.Default, PulseIntervalPreference.Read(ProjectRootA));

            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "Infinity");
            Assert.AreEqual(PulseIntervalPreference.Default, PulseIntervalPreference.Read(ProjectRootA));
        }

        [Test]
        public void ClampsAValueBelowTheMinimum()
        {
            PulseIntervalPreference.Write(ProjectRootA, 0.001f);

            Assert.AreEqual(PulseIntervalPreference.Minimum, PulseIntervalPreference.Read(ProjectRootA));
        }

        [Test]
        public void ClampsAValueAboveTheMaximum()
        {
            PulseIntervalPreference.Write(ProjectRootA, 60f);

            Assert.AreEqual(PulseIntervalPreference.Maximum, PulseIntervalPreference.Read(ProjectRootA));
        }

        /// <remarks>
        /// 직접 고치거나 옛 version 이 쓴 값이 범위 밖일 수 있으므로 읽을 때도 같은 범위로 자른다.
        /// </remarks>
        [Test]
        public void ClampsAnOutOfRangeValueThatWasStoredWithoutGoingThroughWrite()
        {
            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "999");
            Assert.AreEqual(PulseIntervalPreference.Maximum, PulseIntervalPreference.Read(ProjectRootA));

            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "-5");
            Assert.AreEqual(PulseIntervalPreference.Minimum, PulseIntervalPreference.Read(ProjectRootA));
        }

        /// <remarks>
        /// 문화권에 따라 소수점이 쉼표면 "0.25" 를 25 로 읽는다. 저장과 읽기 모두 <c>InvariantCulture</c> 를 쓰는지 확인한다.
        /// </remarks>
        [Test]
        public void StoresTheNumberWithAnInvariantDecimalPoint()
        {
            PulseIntervalPreference.Write(ProjectRootA, 0.5f);

            Assert.AreEqual("0.5", EditorPrefs.GetString(PulseIntervalPreference.KeyFor(ProjectRootA), string.Empty));
        }
    }
}
