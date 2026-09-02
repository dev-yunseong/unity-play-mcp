using Artel.Affordances.Live;
using NUnit.Framework;
using UnityEditor;

namespace Artel.Tests.McpConfig
{
    /// <remarks>
    /// <c>EditorPrefs</c> 는 한 기계의 모든 Unity project 가 함께 쓴다. project 경로로 key 를 가르지 않으면
    /// 한 project 에서 줄인 간격이 다른 project 에도 적용되고, 경로 표기만 다른 같은 project 를 서로 다른
    /// project 로 잘못 셀 수 있다. 이 test 가 만드는 key 는 <see cref="TearDown"/> 에서 반드시 지워, 이 test 를
    /// 돌리는 개발자의 실제 <c>EditorPrefs</c> 를 더럽히지 않는다.
    /// </remarks>
    public sealed class PulseIntervalPreferenceTests
    {
        // 실제 개발자 기계의 project 경로와 겹치지 않도록, 있을 법하지 않은 이름을 쓴다.
        private const string ProjectRootA = "/unity-play-mcp-test-fixture/3f51a9d7-pulse-interval-a";
        private const string ProjectRootB = "/unity-play-mcp-test-fixture/3f51a9d7-pulse-interval-b";

        [TearDown]
        public void TearDown()
        {
            // KeyFor 가 구분자와 꼬리 slash 를 정규화하므로, 이 두 key 만 지워도 이 test 가 심은 값은 전부 없어진다.
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
        /// 다른 기능이 같은 key 를 건드리거나 사람이 손으로 고치면 <c>EditorPrefs</c> 에 숫자가 아닌 문자열이
        /// 남을 수 있다. 그럴 때 예외를 던지는 대신 기본값으로 떨어져야 화면과 channel 이 함께 살아 있다.
        /// </remarks>
        [Test]
        public void FallsBackToTheDefaultWhenEditorPrefsHoldsSomethingThatIsNotANumber()
        {
            EditorPrefs.SetString(PulseIntervalPreference.KeyFor(ProjectRootA), "이해할 수 없는 값");

            Assert.AreEqual(PulseIntervalPreference.Default, PulseIntervalPreference.Read(ProjectRootA));
        }

        /// <remarks>
        /// NaN 과 무한대는 숫자로 읽히지만 어느 끝으로도 자를 수 없다. 비교가 전부 false 라 clamp 를 그대로
        /// 통과하고, 그 값이 <c>Pulse.Begin</c> 에 닿으면 channel 이 조용히 시작하지 않는다.
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
        /// 저장할 때 잘라내는 것만으로는 부족하다. 사람이 <c>EditorPrefs</c> 를 직접 고치거나 옛 version 이
        /// 다른 범위로 써 둔 값이 남아 있을 수 있어, 읽는 쪽도 같은 규칙을 다시 건다.
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
        /// 저장 형식이 문화권을 타면 소수점이 쉼표인 기계에서 "0.25" 를 25 로 읽는다. 저장과 읽기 둘 다
        /// <c>InvariantCulture</c> 를 쓰는지 확인한다.
        /// </remarks>
        [Test]
        public void StoresTheNumberWithAnInvariantDecimalPoint()
        {
            PulseIntervalPreference.Write(ProjectRootA, 0.5f);

            Assert.AreEqual("0.5", EditorPrefs.GetString(PulseIntervalPreference.KeyFor(ProjectRootA), string.Empty));
        }
    }
}
