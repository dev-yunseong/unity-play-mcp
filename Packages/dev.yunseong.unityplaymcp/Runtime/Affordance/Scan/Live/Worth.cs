using System;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 테스트가 작용할 수 있는 객체인지 — 리포트의 규칙을, 초당 열 번 묻는다.
    /// </summary>
    /// <remarks>
    /// 규칙은 스캔의 것이고 새로 만드는 대신 여기 복사한다: 객체는 그 컴포넌트 중 하나가 구워진 근거를 나르거나 인스펙터로
    /// 연결된 호출을 가질 때 센다. 그것이 리포트의 목록을 천이 아니라 마흔셋으로 만드는 것이고, 거기에
    /// <c>Canvas/ExitButton</c> 을 넣는 것이다 — <c>onClick</c> 이 메서드를 가리키는 Button.
    ///
    /// 같은 규칙이어야 한다. 명세는 리포트 자신의 순회에서 쓰였으므로, 선을 다른 데 긋는 판독은 패키지가 답할 수 있는 줄을
    /// 확인 불가로 보고하거나 판독을 배경으로 가득 채운다. 둘이 달라도 되는 자리가 하나 있는데, 그것도 한 방향으로만이다:
    /// 감시 대상 멤버를 쥔 객체는 다른 무엇도 자격이 없더라도 쓴다. 아무도 찾을 수 없는 값이 배경 한 줄보다 나쁘기
    /// 때문이다.
    ///
    /// 객체마다 한 번 답하고 기억한다. 컴포넌트의 UnityEvent 필드를 읽는 일은 리플렉션이고, 스캔은 씬마다 한 번 치르는
    /// 값을 이쪽은 매 박자마다 치르게 된다. 타입이 아니라 객체에 대고 기억한다: 한 타입의 Button 둘은 서로 다르게
    /// 연결돼 있고, 그중 하나는 아무것도 가리키지 않을 수 있다.
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

        private static readonly Dictionary<int, bool> Answered = new Dictionary<int, bool>();

        internal static bool Writing(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            if (subject == null)
            {
                return false;
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

        private static bool Ask(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                // 스캔은 이것을 씬에 대한 공백으로 보고한다. 여기서는 그저 아무 말도 할 수 없는 객체일 뿐이다.
                return false;
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
                    return true;
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
                    return true;
                }
            }

            return false;
        }

        internal static void Forget()
        {
            Answered.Clear();
        }
    }
}
