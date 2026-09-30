using System;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 프레임타임을 링버퍼에 모으고, 요청받은 시점에 분포로 접는다.
    ///
    /// 집계 주기는 <see cref="TrySummarize"/> 를 부르는 쪽이 정한다. 여기에 타이머를 두면
    /// 전송 주기와 어긋나 스냅샷이 중복되거나 구간이 버려진다.
    ///
    /// Unity API 를 직접 읽지 않아 에디터 없이 테스트할 수 있다. 구동은 <c>UnityPlayMcpHost.Update</c> 가 한다.
    /// <see cref="Record"/> 는 매 프레임 불리므로 힙 할당을 하지 않는다.
    /// </summary>
    internal sealed class FrameTimeRecorder
    {
        /// <summary>
        /// 60fps 기준 10초. 넘치면 오래된 샘플부터 밀려난다. 실제로 덮은 시간은
        /// <see cref="FrameTimeStatistics.SampledSeconds"/> 가 알려 준다.
        /// </summary>
        private const int DefaultCapacity = 600;

        /// <summary>예산의 몇 배부터 hitch로 셀지.</summary>
        private const float HitchBudgetMultiplier = 2f;

        /// <summary>예산이 0 이하일 때 쓰는 값. 0 이면 모든 프레임이 hitch 가 된다.</summary>
        private const float FallbackBudgetSeconds = 1f / 60f;

        private readonly float[] samples;
        private readonly float[] scratch;

        private int writeIndex;
        private int count;
        private bool discardedFirstFrame;

        public FrameTimeRecorder(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), "Sample capacity must be greater than zero.");
            }

            samples = new float[capacity];
            scratch = new float[capacity];
        }

        public void Record(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            // 첫 프레임의 unscaledDeltaTime은 씬 로드 시간을 포함해 렌더 비용이 아니다.
            // 남기면 모든 세션이 hitch 1로 시작한다.
            if (!discardedFirstFrame)
            {
                discardedFirstFrame = true;
                return;
            }

            samples[writeIndex] = deltaSeconds;
            writeIndex = (writeIndex + 1) % samples.Length;
            if (count < samples.Length)
            {
                count++;
            }
        }

        /// <summary>
        /// 모은 샘플을 분포로 접고 창을 비운다. 연속 호출한 구간끼리 겹치지 않는다.
        /// </summary>
        /// <returns>
        /// 샘플이 없으면 false 다. 빈 통계는 0fps 로 읽힌다.
        /// </returns>
        public bool TrySummarize(float budgetSeconds, out FrameTimeStatistics statistics)
        {
            statistics = default;

            if (count == 0)
            {
                return false;
            }

            statistics = Summarize(budgetSeconds);

            // writeIndex까지 되돌려 다음 창을 배열 앞부터 채운다.
            count = 0;
            writeIndex = 0;
            return true;
        }

        private FrameTimeStatistics Summarize(float budgetSeconds)
        {
            if (budgetSeconds <= 0f)
            {
                budgetSeconds = FallbackBudgetSeconds;
            }

            var hitchThresholdSeconds = budgetSeconds * HitchBudgetMultiplier;

            // 합과 hitch는 순서와 무관하므로 정렬 전에 원본을 한 번 훑는다.
            var totalSeconds = 0f;
            var hitchCount = 0;
            for (var i = 0; i < count; i++)
            {
                var sample = samples[i];
                totalSeconds += sample;
                if (sample > hitchThresholdSeconds)
                {
                    hitchCount++;
                }
            }

            // Array.Sort 의 introsort 는 힙 할당을 하지 않는다.
            Array.Copy(samples, 0, scratch, 0, count);
            Array.Sort(scratch, 0, count);

            return new FrameTimeStatistics(
                count,
                totalSeconds,
                totalSeconds / count,
                scratch[0],
                scratch[count - 1],
                PercentileSeconds(0.95f),
                PercentileSeconds(0.99f),
                LowFps(100),
                LowFps(1000),
                hitchCount,
                hitchThresholdSeconds,
                budgetSeconds);
        }

        /// <summary>
        /// 보간 없는 nearest-rank. 실제로 관측한 프레임타임만 돌려준다.
        /// </summary>
        private float PercentileSeconds(float percentile)
        {
            var rank = (int)Math.Ceiling(percentile * count) - 1;
            if (rank < 0)
            {
                rank = 0;
            }
            else if (rank > count - 1)
            {
                rank = count - 1;
            }

            return scratch[rank];
        }

        /// <param name="fraction">100이면 최악 1%, 1000이면 최악 0.1%.</param>
        private float LowFps(int fraction)
        {
            // 올림한다. 샘플이 fraction 보다 적어도 한 프레임은 본다.
            var worstCount = (count + fraction - 1) / fraction;
            if (worstCount < 1)
            {
                worstCount = 1;
            }

            var totalSeconds = 0f;
            for (var i = count - worstCount; i < count; i++)
            {
                totalSeconds += scratch[i];
            }

            var meanSeconds = totalSeconds / worstCount;
            return meanSeconds > 0f ? 1f / meanSeconds : 0f;
        }
    }
}
