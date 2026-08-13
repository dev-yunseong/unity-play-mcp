using System;

namespace Artel.Diagnostics
{
    /// <summary>
    /// 프레임타임을 링버퍼에 모아 집계 주기마다 분포로 접는다.
    ///
    /// <c>SceneStatePoller</c>와 같은 모양이다. Unity API를 직접 읽지 않고 값으로 받아
    /// 에디터 없이 테스트할 수 있게 두고, 구동은 호출자(<c>ArtelManager.Update</c>)가 맡는다.
    ///
    /// 매 프레임 도는 자리라 <see cref="Record"/>는 힙 할당을 하지 않는다. 샘플 버퍼와 정렬용
    /// 스크래치를 생성자에서 한 번만 잡고 계속 재사용한다.
    /// </summary>
    internal sealed class FrameTimeRecorder
    {
        /// <summary>60fps 기준 10초. 5초 주기에서는 넉넉하다.</summary>
        private const int DefaultCapacity = 600;

        /// <summary>예산의 몇 배부터 hitch로 셀지.</summary>
        private const float HitchBudgetMultiplier = 2f;

        /// <summary>예산 해석이 실패했을 때 쓰는 값. 0이 들어오면 모든 프레임이 hitch가 된다.</summary>
        private const float FallbackBudgetSeconds = 1f / 60f;

        private readonly float intervalSeconds;
        private readonly float[] samples;
        private readonly float[] scratch;

        private int writeIndex;
        private int count;
        private bool discardedFirstFrame;
        private bool hasDeadline;
        private float nextAggregateTime;

        public FrameTimeRecorder(float intervalSeconds, int capacity = DefaultCapacity)
        {
            if (intervalSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervalSeconds), "Aggregate interval must be greater than zero.");
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), "Sample capacity must be greater than zero.");
            }

            this.intervalSeconds = intervalSeconds;
            samples = new float[capacity];
            scratch = new float[capacity];
        }

        /// <param name="counted">
        /// 이 프레임을 분포에 넣을지. 호출자는 <c>Application.isFocused</c>를 넘긴다. Unity가
        /// 포커스를 잃은 창의 프레임 페이싱을 스로틀링하므로, 섞으면 hitch가 폭증하고 평균이 무너진다.
        /// </param>
        public void Record(float deltaSeconds, bool counted)
        {
            if (!counted || deltaSeconds <= 0f)
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

        /// <param name="resolveBudgetSeconds">
        /// 프레임 예산을 초 단위로 돌려주는 델리게이트. 값이 아니라 델리게이트인 이유는 예산 해석이
        /// <c>Application.targetFrameRate</c>·<c>Screen.currentResolution</c>을 읽기 때문이다.
        /// 값으로 받으면 집계하지 않는 프레임에서도 매번 읽게 된다.
        /// </param>
        public bool TryAggregate(
            float currentTime,
            Func<float> resolveBudgetSeconds,
            out FrameTimeStatistics statistics)
        {
            if (resolveBudgetSeconds == null)
            {
                throw new ArgumentNullException(nameof(resolveBudgetSeconds));
            }

            statistics = default;

            // 첫 호출이 기준 시각을 세운다. 호출자가 별도로 초기화하지 않아도 되게 하려는 것.
            if (!hasDeadline)
            {
                hasDeadline = true;
                nextAggregateTime = currentTime + intervalSeconds;
                return false;
            }

            if (currentTime < nextAggregateTime)
            {
                return false;
            }

            nextAggregateTime = currentTime + intervalSeconds;

            // 구간 내내 포커스가 없었던 경우. 빈 통계를 내보내면 소비자가 0fps로 읽는다.
            if (count == 0)
            {
                return false;
            }

            statistics = Summarize(resolveBudgetSeconds());

            // 구간은 겹치지 않는다. writeIndex까지 되돌려 다음 구간을 배열 앞부터 채운다.
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

            // Array.Sort의 introsort 경로는 힙을 잡지 않는다. 집계 시점에만 도는 비용이다.
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
        /// 보간 없는 nearest-rank. 샘플이 적어도 실제로 관측한 프레임타임만 돌려주므로,
        /// 존재하지 않는 중간값을 만들어 내지 않는다.
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
            // 올림. 샘플이 fraction보다 적어도 최소 한 프레임은 본다.
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
