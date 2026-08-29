using System;
using Artel.Diagnostics;
using NUnit.Framework;

namespace Artel.Tests.Diagnostics
{
    public sealed class FrameTimeRecorderTests
    {
        private const float BudgetSeconds = 1f / 60f;

        /// <summary>60fps 한 프레임. 예산과 같으므로 hitch가 아니다.</summary>
        private const float SmoothFrameSeconds = 1f / 60f;

        /// <summary>예산의 6배. hitch 기준(2배)을 확실히 넘는다.</summary>
        private const float HitchFrameSeconds = 0.1f;

        /// <summary>
        /// 첫 <c>Record</c> 한 건은 씬 로드 시간이 실려 있어 버려진다. 테스트마다 그 규칙을
        /// 반복하지 않도록 여기서 소화한다.
        /// </summary>
        private static FrameTimeRecorder Started(int capacity = 600)
        {
            var recorder = new FrameTimeRecorder(capacity);
            recorder.Record(SmoothFrameSeconds);
            return recorder;
        }

        private static void RecordFrames(FrameTimeRecorder recorder, int frameCount, float deltaSeconds)
        {
            for (var i = 0; i < frameCount; i++)
            {
                recorder.Record(deltaSeconds);
            }
        }

        [Test]
        public void Constructor_RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTimeRecorder(0));
        }

        [Test]
        public void TrySummarize_ReturnsFalseWithoutSamples()
        {
            var recorder = Started();

            Assert.IsFalse(recorder.TrySummarize(BudgetSeconds, out _));
        }

        [Test]
        public void TrySummarize_SummarizesUniformFrames()
        {
            var recorder = Started();
            RecordFrames(recorder, 60, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(60, statistics.FrameCount);
            Assert.AreEqual(SmoothFrameSeconds, statistics.MeanSeconds, 1e-6f);
            Assert.AreEqual(SmoothFrameSeconds, statistics.MinSeconds, 1e-6f);
            Assert.AreEqual(SmoothFrameSeconds, statistics.MaxSeconds, 1e-6f);
            Assert.AreEqual(60 * SmoothFrameSeconds, statistics.SampledSeconds, 1e-4f);
            Assert.AreEqual(0, statistics.HitchCount);
            Assert.AreEqual(BudgetSeconds, statistics.BudgetSeconds, 1e-6f);
            Assert.AreEqual(BudgetSeconds * 2f, statistics.HitchThresholdSeconds, 1e-6f);
        }

        [Test]
        public void TrySummarize_CountsFramesOverTwiceTheBudgetAsHitches()
        {
            var recorder = Started();
            RecordFrames(recorder, 59, SmoothFrameSeconds);
            recorder.Record(HitchFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(1, statistics.HitchCount);
            Assert.AreEqual(HitchFrameSeconds, statistics.MaxSeconds, 1e-6f);
        }

        [Test]
        public void TrySummarize_FallsBackToSixtyFpsWhenTheBudgetIsNotPositive()
        {
            var recorder = Started();
            RecordFrames(recorder, 10, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(0f, out var statistics));

            // 예산 0을 그대로 쓰면 문턱이 0이 되어 모든 프레임이 hitch가 된다.
            Assert.AreEqual(1f / 60f, statistics.BudgetSeconds, 1e-6f);
            Assert.AreEqual(0, statistics.HitchCount);
        }

        [Test]
        public void Record_DropsTheFirstFrameSoSceneLoadIsNotAHitch()
        {
            var recorder = new FrameTimeRecorder();

            // 씬 로드 시간이 실린 첫 프레임.
            recorder.Record(HitchFrameSeconds);
            RecordFrames(recorder, 10, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(10, statistics.FrameCount);
            Assert.AreEqual(0, statistics.HitchCount);
        }

        [Test]
        public void Record_IncludesEveryFrame()
        {
            var recorder = Started();
            RecordFrames(recorder, 10, SmoothFrameSeconds);
            recorder.Record(HitchFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(11, statistics.FrameCount);
            Assert.AreEqual(1, statistics.HitchCount);
        }

        [Test]
        public void Record_IgnoresNonPositiveDeltas()
        {
            var recorder = Started();
            recorder.Record(0f);
            recorder.Record(-1f);
            RecordFrames(recorder, 5, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(5, statistics.FrameCount);
        }

        [Test]
        public void TrySummarize_PutsOnlyTheWorstFramesAboveTheNinetyNinthPercentile()
        {
            // 100 샘플에서 nearest-rank p99는 아래에서 99번째, 즉 최악에서 두 번째 프레임이다.
            // 나쁜 프레임이 하나뿐이면 p99가 아니라 최대값만 움직이므로 두 개를 넣는다.
            var recorder = Started();
            RecordFrames(recorder, 98, SmoothFrameSeconds);
            RecordFrames(recorder, 2, HitchFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(100, statistics.FrameCount);
            Assert.AreEqual(HitchFrameSeconds, statistics.Percentile99Seconds, 1e-6f);
            Assert.AreEqual(SmoothFrameSeconds, statistics.Percentile95Seconds, 1e-6f);
        }

        [Test]
        public void TrySummarize_DerivesLowFpsFromTheWorstFrames()
        {
            var recorder = Started();
            RecordFrames(recorder, 99, SmoothFrameSeconds);
            recorder.Record(HitchFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            // 100 샘플이면 최악 1%는 1프레임. 0.1%도 올림 탓에 같은 1프레임이라 값이 같아진다.
            Assert.AreEqual(1f / HitchFrameSeconds, statistics.OnePercentLowFps, 1e-3f);
            Assert.AreEqual(1f / HitchFrameSeconds, statistics.PointOnePercentLowFps, 1e-3f);
        }

        [Test]
        public void TrySummarize_DoesNotCarrySamplesIntoTheNextWindow()
        {
            var recorder = Started();
            RecordFrames(recorder, 10, HitchFrameSeconds);
            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var first));
            Assert.AreEqual(10, first.HitchCount);

            RecordFrames(recorder, 5, SmoothFrameSeconds);
            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var second));

            Assert.AreEqual(5, second.FrameCount);
            Assert.AreEqual(0, second.HitchCount);
            Assert.AreEqual(SmoothFrameSeconds, second.MaxSeconds, 1e-6f);
        }

        [Test]
        public void TrySummarize_ReturnsFalseOnASecondCallWithNoNewFrames()
        {
            var recorder = Started();
            RecordFrames(recorder, 5, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out _));
            Assert.IsFalse(recorder.TrySummarize(BudgetSeconds, out _));
        }

        [Test]
        public void Record_KeepsTheMostRecentFramesWhenCapacityIsExceeded()
        {
            var recorder = Started(capacity: 4);
            RecordFrames(recorder, 4, HitchFrameSeconds);
            RecordFrames(recorder, 4, SmoothFrameSeconds);

            Assert.IsTrue(recorder.TrySummarize(BudgetSeconds, out var statistics));

            Assert.AreEqual(4, statistics.FrameCount);
            Assert.AreEqual(0, statistics.HitchCount);
            Assert.AreEqual(SmoothFrameSeconds, statistics.MaxSeconds, 1e-6f);
        }
    }
}
