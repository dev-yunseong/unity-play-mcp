using System;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 객체를 pulse 에 쓸지 판정한다. 스캔 리포트와 같은 규칙이다.
    /// </summary>
    /// <remarks>
    /// 객체의 컴포넌트 중 하나가 감시 대상 멤버나 구운 <c>evidence</c> 를 갖거나 인스펙터로 연결된 호출을 가지면 쓴다.
    /// 규칙이 스캔과 다르면 리포트가 답할 수 있는 줄을 확인 불가로 보고하거나 pulse 가 배경으로 채워진다. 예외는 감시 대상
    /// 멤버 하나로, 다른 자격이 없어도 쓴다.
    ///
    /// UnityEvent 필드를 읽는 리플렉션을 매 pulse 반복하지 않도록 객체마다 답을 기억한다. 같은 타입의 Button 도 연결이
    /// 다를 수 있어 타입이 아니라 객체 단위로 기억한다.
    ///
    /// 위 조건에 걸리지 않아도 <see cref="Drawn"/> 컴포넌트를 단 객체는 쓴다. <c>SceneEvidenceScan</c> 의 객체 admission
    /// 도 같은 조건을 쓴다. 작용 대상이 라벨과 그림에 묻히지 않도록 컴포넌트 목록에는 넣지 않는다.
    /// </remarks>
    internal static class Worth
    {
        /// <summary>
        /// 캐시를 비우기 전까지 기억하는 답의 수.
        /// </summary>
        /// <remarks>
        /// 객체를 계속 만들고 부수는 게임에서 캐시가 끝없이 자라지 않게 한다. 비워도 다시 계산할 뿐 답은 틀리지 않는다.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, Admitted> Answered = new Dictionary<int, Admitted>();

        /// <summary>객체를 쓰는 이유.</summary>
        /// <remarks>
        /// 실을 객체 수의 예산을 <c>evidence</c> 객체와 화면 요소로 나누기 위해 구분한다.
        /// </remarks>
        internal enum Admitted
        {
            /// <summary>쓰지 않는다.</summary>
            No,

            /// <summary>감시 멤버, 구운 <c>evidence</c>, 인스펙터로 연결된 호출 중 하나가 있다.</summary>
            Evidence,

            /// <summary><see cref="Drawn"/> 컴포넌트만 있다.</summary>
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
                // 스캔은 이것을 공백으로 보고한다. 여기서는 쓰지 않는다.
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

            // Drawn 판정은 맨 뒤에 둔다. `Image` 를 얹은 `Button` 처럼 evidence 도 있는 객체는 Evidence 로 세야
            // 예산 구분이 유지된다.
            return Drawn.Any(subject) ? Admitted.Drawn : Admitted.No;
        }

        internal static void Forget()
        {
            Answered.Clear();
        }
    }
}
