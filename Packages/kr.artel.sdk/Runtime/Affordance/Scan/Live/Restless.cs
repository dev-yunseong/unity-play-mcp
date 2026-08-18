using System.Collections.Generic;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 연속적인 값이 실제로 어디론가 가기 전까지는 가만히 있게 한다.
    /// </summary>
    /// <remarks>
    /// 변화 게이트는 판독 전체를 비교하므로, 결코 반복되지 않는 값은 매 박자마다 그것을 열어젖히고 게이트는 아무 뜻도
    /// 없어진다 — 모든 판독을 받은 독자는 그중 무엇이 소식이었는지를 스스로 알아내야 하는데, 그것이 게이트가 하라고
    /// 존재하는 일이다.
    ///
    /// 그렇게 구는 값이 위치다. 게임이 다시 계산하는 숫자를 쥔 필드나 물리 솔버가 계속 밀어 대는 객체는, 있던 자리에 정확히
    /// 그대로 앉아 있으면서 마지막 소수 자리가 영원히 달라진다.
    ///
    /// 이것은 반올림인데, 그 격자가 0 이 아니라 값이 마지막으로 멈춘 자리에 고정돼 있다. 한 마디 할 값만큼 가지 못한 값은
    /// 이미 보낸 숫자로 되읽히므로 판독이 일치하고 게이트는 닫힌 채로 있다. 일단 가고 나면 새 숫자를 취해 그것이 다음
    /// 고정점이 된다. 누적되는 것은 없다: 느린 표류도 결국 경계를 넘고 보고된다. 느리게 멀리 간 것도 간 것이기 때문이다.
    ///
    /// 진짜로 이동 중인 값을 붙잡아 두지 않고, 붙잡아서도 안 된다. 다음 스테이지로 미끄러지는 맵 커서는 움직이는 매 박자마다
    /// 게이트를 열고, 그것이 소식이다. 거기서 트래픽을 가두는 것은 박자 자체이고, 그것이 매 프레임 씬 전체를 해싱하는
    /// SDK 와의 차이다.
    /// </remarks>
    internal sealed class Restless
    {
        /// <summary>
        /// 좌표가 말할 값이 있으려면 얼마나 가야 하는지.
        /// </summary>
        /// <remarks>
        /// 추측이고, 추측이라고 말한다. 그것은 게임 자신의 월드 단위인데 어느 패키지도 그 축척을 알 수 없다 — 한 프로젝트의
        /// 1 밀리미터가 다른 프로젝트에서는 화면 너비다. 명세가 비교하는 무엇도 그 아래 숨지 못할 만큼 작게 골랐다. 근거가
        /// 위치로 하는 일은 한 객체가 다른 객체가 있는 자리에 도착했는지를 묻는 것이고, 서로에게서 대입된 두 객체는 거의가
        /// 아니라 정확히 같기 때문이다.
        ///
        /// 그것이 맞는지는 논쟁이 아니라 측정의 문제다: 판독이 거의 다 나가는 실행은 어떤 조건도 언급하지 않는 이유로 움직이는
        /// 값을 가진 것이고, 판독이 그것이 무엇이었는지 말해 준다.
        /// </remarks>
        private const float Bound = 0.001f;

        private readonly Dictionary<string, float> _standing = new Dictionary<string, float>();

        /// <summary>
        /// 쓸 숫자: 아무 일도 없었으면 이미 보낸 것, 아니면 새 것.
        /// </summary>
        internal float Settle(string key, float now)
        {
            if (_standing.TryGetValue(key, out var standing))
            {
                if (now >= standing - Bound && now <= standing + Bound)
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
