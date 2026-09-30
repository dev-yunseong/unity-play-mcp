using System.Collections.Generic;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 연속 값이 경계 이상 움직이기 전까지는 마지막으로 보낸 값을 유지한다.
    /// </summary>
    /// <remarks>
    /// 변화 판정은 pulse 전체를 비교하므로, 물리나 재계산으로 마지막 소수 자리만 흔들리는 위치 값이 있으면 매 pulse 가
    /// 변화로 판정된다.
    ///
    /// 기준점은 0 이 아니라 마지막으로 보낸 값이다. 경계 안의 값은 보낸 값으로 되돌려지고, 경계를 넘으면 새 값이 기준점이
    /// 된다. 느린 표류도 결국 경계를 넘어 보고된다. 실제로 움직이는 값은 매 pulse 보고되고, 트래픽은 <c>pulse</c> 간격이
    /// 제한한다.
    /// </remarks>
    internal sealed class Restless
    {
        /// <summary>
        /// 새 값으로 보려면 움직여야 하는 거리. 값과 같은 단위다.
        /// </summary>
        /// <remarks>
        /// 월드 단위의 축척은 게임마다 달라 기본값 0.001 은 추정치다. <c>evidence</c> 가 비교하는 위치는 서로 대입된 값이라
        /// 정확히 같으므로 작게 잡았다.
        ///
        /// 화면 픽셀처럼 축척이 정해진 값은 호출하는 쪽이 경계(예: 1)를 생성자로 넘긴다.
        /// </remarks>
        private readonly float _bound;

        private readonly Dictionary<string, float> _standing = new Dictionary<string, float>();

        internal Restless() : this(0.001f)
        {
        }

        internal Restless(float bound)
        {
            _bound = bound;
        }

        /// <summary>
        /// 경계 안이면 이미 보낸 값을, 아니면 <paramref name="now"/> 를 돌려준다.
        /// </summary>
        internal float Settle(string key, float now)
        {
            if (_standing.TryGetValue(key, out var standing))
            {
                if (now >= standing - _bound && now <= standing + _bound)
                {
                    return standing;
                }
            }

            _standing[key] = now;
            return now;
        }

        internal void Forget()
        {
            _standing.Clear();
        }
    }
}
