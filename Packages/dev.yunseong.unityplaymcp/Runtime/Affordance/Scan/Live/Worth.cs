using System;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 테스트가 작용할 수 있는 객체인지 — 리포트의 규칙을, 초당 열 번 묻는다.
    /// </summary>
    /// <remarks>
    /// 규칙은 스캔의 것이고 새로 만드는 대신 여기 복사한다: 객체는 그 컴포넌트 중 하나가 구워진 근거를 나르거나 인스펙터로
    /// 연결된 호출을 가질 때 센다. 그것이 리포트의 목록을 천이 아니라 마흔셋으로 만드는 것이고, 거기에
    /// <c>Canvas/ExitButton</c> 을 넣는 것이다 — <c>onClick</c> 이 메서드를 가리키는 Button.
    ///
    /// 같은 규칙이어야 한다. 명세는 리포트 자신의 순회에서 쓰였으므로, 선을 다른 데 긋는 pulse 는 패키지가 답할 수 있는 줄을
    /// 확인 불가로 보고하거나 pulse 를 배경으로 가득 채운다. 둘이 달라도 되는 자리가 하나 있는데, 그것도 한 방향으로만이다:
    /// 감시 대상 멤버를 쥔 객체는 다른 무엇도 자격이 없더라도 쓴다. 아무도 찾을 수 없는 값이 배경 한 줄보다 나쁘기
    /// 때문이다.
    ///
    /// 객체마다 한 번 답하고 기억한다. 컴포넌트의 UnityEvent 필드를 읽는 일은 리플렉션이고, 스캔은 씬마다 한 번 치르는
    /// 값을 이쪽은 매 박자마다 치르게 된다. 타입이 아니라 객체에 대고 기억한다: 한 타입의 Button 둘은 서로 다르게
    /// 연결돼 있고, 그중 하나는 아무것도 가리키지 않을 수 있다.
    ///
    /// 네 번째 길이 있고, 그것은 게임 코드에 관한 것이 아니다: <see cref="Drawn"/> 이 아는 컴포넌트 — 라벨, 그림,
    /// 슬라이더 — 를 단 객체는 감시 멤버가 없어도 쓴다. 앞의 셋은 전부 게임 코드에 관한 것이라 <c>UnityEngine.UI</c> 를
    /// 하나도 통과시키지 못했고, 그래서 점수를 띄우는 <c>Text</c> 와 체력을 채우는 <c>Image</c> 는 모든 pulse 에서
    /// 빠졌다. 화면에 무엇이 보이는지를 물을 방법이 없었던 것이 그 이유다.
    ///
    /// 이 네 번째 길은 위의 규칙을 옮기므로 리포트의 목록도 같이 넓어진다 — <c>SceneEvidenceScan</c> 의 객체 admission 이
    /// 같은 물음을 같이 묻는다. 컴포넌트 목록은 안 넓혔다: 라벨과 그림을 컴포넌트로 쓰면 정작 작용 대상인 몇 개가 그
    /// 아래 파묻힌다는 그쪽의 이유가 그대로 유효하다.
    /// </remarks>
    internal static class Worth
    {
        /// <summary>
        /// 전부 버리고 다시 알아내기 전까지 답을 몇 개나 쥐고 있는지.
        /// </summary>
        /// <remarks>
        /// 한 시간 동안 만들고 부수는 게임은 그러지 않으면 여태 만든 객체마다 여기에 줄 하나씩을 늘린다. 전부 버리는 값은
        /// 비싼 순회 한 번이고 틀린 답을 줄 수는 없는데, 대안이 누수일 때 택할 거래가 그것이다.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, Admitted> Answered = new Dictionary<int, Admitted>();

        /// <summary>객체를 들인 이유. 어느 길로 들어왔는지가 곧 그것이 무엇인지다.</summary>
        /// <remarks>
        /// 부르는 쪽이 예 아니오만 알면 되던 시절에는 bool 이었다. 지금은 무엇이 몇 개까지 실릴지의 예산을 둘로 나눠야
        /// 하고, 그러려면 이 객체가 근거 때문에 들어왔는지 화면에 무언가를 그려서 들어왔는지를 가려야 한다.
        /// </remarks>
        internal enum Admitted
        {
            /// <summary>아무 길로도 들어오지 못했다.</summary>
            No,

            /// <summary>게임 코드에 관한 앞의 세 길 중 하나. 감시 멤버, 구운 근거, 인스펙터로 연결된 호출.</summary>
            Evidence,

            /// <summary>화면에 무언가를 그리는 컴포넌트를 나른다. 그것 말고는 아무 이유도 없다.</summary>
            Drawn
        }

        internal static Admitted Writing(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            if (subject == null)
            {
                return Admitted.No;
            }

            var id = subject.GetInstanceID();

            if (Answered.TryGetValue(id, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var answer = Ask(subject, byOwner);
            Answered[id] = answer;
            return answer;
        }

        private static Admitted Ask(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                // 스캔은 이것을 씬에 대한 공백으로 보고한다. 여기서는 그저 아무 말도 할 수 없는 객체일 뿐이다.
                return Admitted.No;
            }

            var calls = new List<PersistentCall>();

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (byOwner.ContainsKey(type) || AffordanceCatalog.For(type) != null)
                {
                    return Admitted.Evidence;
                }

                calls.Clear();

                try
                {
                    PersistentCallReader.Read(component, calls);
                }
                catch (Exception)
                {
                    continue;
                }

                if (calls.Count > 0)
                {
                    return Admitted.Evidence;
                }
            }

            // 네 번째 길은 맨 뒤다. 근거를 나르면서 동시에 라벨을 단 객체 — `Image` 를 얹은 `Button` — 는 근거 쪽으로
            // 세어야 하고, 이것을 앞에 두면 그런 객체가 전부 화면 요소가 되어 예산 구분이 뜻을 잃는다. 값은 객체마다
            // 한 번이고 그 뒤로는 캐시가 답한다.
            return Drawn.Any(subject) ? Admitted.Drawn : Admitted.No;
        }

        internal static void Forget()
        {
            Answered.Clear();
        }
    }
}
