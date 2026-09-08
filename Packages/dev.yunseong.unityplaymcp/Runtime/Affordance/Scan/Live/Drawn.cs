using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 플레이어가 화면에서 읽는 것 — 라벨, 그림, 슬라이더 — 을 나르는 컴포넌트.
    /// </summary>
    /// <remarks>
    /// 이것들은 게임 코드에 관한 것이 하나도 아니다. <c>UnityEngine.UI</c> 와 <c>TMPro</c> 는 Unity 자신의 어셈블리라 구운
    /// 근거가 없고, 감시 대상 멤버를 소유하지도 않으며, 대개 인스펙터로 연결된 호출도 없다. 그래서 <see cref="Worth"/> 의 세
    /// 길 어디에도 걸리지 않았고, 점수를 띄우는 <c>Text</c> 와 체력을 채우는 <c>Image</c> 는 pulse 에 한 번도 실린 적이 없다.
    ///
    /// 뿌리가 둘인 이유: <c>Slider</c> 는 <c>Graphic</c> 이 아니라 <c>Selectable</c> 이다. 하나만 보면 슬라이더와 토글과
    /// 드롭다운이 통째로 빠진다.
    ///
    /// 타입을 컴파일 대상으로 삼는 대신 이름으로 맞춘다. uGUI 와 TextMeshPro 는 프로젝트에 없을 수 있는 패키지이고 이
    /// 어셈블리는 둘 다 참조하지 않는다 — <see cref="Scan.SceneEvidenceScan"/> 이 같은 이유로 이미 그렇게 한다.
    ///
    /// 여기서 분류하지 않는다. SDK 는 타입 이름을 <c>by[].on</c> 에 실어 보낼 뿐이고, 무엇이 "보이는 요소" 인지의 판단은
    /// MCP process 가 한다. 그래야 목록을 고치는 데 Unity 재빌드가 필요 없다.
    /// </remarks>
    internal static class Drawn
    {
        /// <summary>이 셋 중 하나를 상속하면 화면에 무언가를 내놓는 컴포넌트로 본다.</summary>
        private static readonly string[] Roots =
        {
            "UnityEngine.UI.Graphic", "UnityEngine.UI.Selectable", "TMPro.TMP_Text"
        };

        /// <summary>
        /// 그 컴포넌트에서 읽어 낼 것들. 그것이 지금 무엇을 보이고 있는가.
        /// </summary>
        /// <remarks>
        /// 글자(<c>text</c>), 채움 비율(<c>fillAmount</c>, <c>normalizedValue</c>), 값(<c>value</c>, <c>isOn</c>) — 리포트가
        /// 라벨과 그림에 대해 묻는 것과 같은 물음이다.
        ///
        /// 뒤의 둘은 그 셋에 없는데도 있다. <c>RawImage</c> 는 앞의 어느 것도 갖지 않고 <c>Button</c> 도 그렇다. 그런
        /// 컴포넌트는 멤버가 하나도 없어 <c>by</c> 항목이 아예 안 써지고(<see cref="LiveState"/> 의 <c>count > 0</c>),
        /// 그러면 타입 이름이 나가지 않아 읽는 쪽이 그 요소를 볼 방법이 없다. <c>texture</c> 와 <c>interactable</c> 이 그
        /// 구멍을 막는다.
        ///
        /// <c>interactable</c> 은 그 밖에도 명세가 직접 묻는 값이다 — <em>계속 버튼이 비활성으로 보인다</em>. 다만 이것은
        /// 컴포넌트 제 스위치이고, <c>click</c> 이 대상을 거절할 때 쓰는 <c>Selectable.IsInteractable()</c>
        /// (<c>TargetLookup</c>) 과 같지 않다: 후자는 부모 <c>CanvasGroup</c> 까지 본다. 메서드라 여기서 걸을 수 없다.
        ///
        /// <c>color</c> 는 안 싣는다. <c>Color</c> 는 struct 라 읽는 쪽이 <c>{"is":"UnityEngine.Color"}</c> 만 받게 되는데,
        /// 그것은 아무 말도 하지 않으면서 자리만 차지한다.
        /// </remarks>
        private static readonly string[] Names =
        {
            "text", "fillAmount", "value", "normalizedValue", "isOn", "texture", "interactable"
        };

        /// <summary>
        /// 전부 버리고 다시 알아내기 전까지 객체를 몇 개나 기억하는지.
        /// </summary>
        /// <remarks>
        /// <see cref="Worth"/> 가 하는 것과 같은 거래다. 한 시간 동안 만들고 부수는 게임은 그러지 않으면 여태 만든 객체마다
        /// 여기에 줄 하나씩을 늘린다.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, bool> Answered = new Dictionary<int, bool>();

        /// <summary>이 객체가 그런 컴포넌트를 하나라도 나르는가. 객체마다 한 번 답하고 기억한다.</summary>
        /// <remarks>
        /// 초당 열 번 도는 자리라 매번 컴포넌트를 훑을 수 없다. 객체 위에 어떤 타입이 있는지는 그것이 만들어질 때 정해진다는
        /// 전제이고, <see cref="LiveState"/> 의 offer 캐시가 이미 같은 전제로 서 있다.
        /// </remarks>
        internal static bool Any(GameObject subject)
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

            var answer = Ask(subject);
            Answered[id] = answer;
            return answer;
        }

        private static bool Ask(GameObject subject)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (component != null && Is(component.GetType()))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool Is(Type type)
        {
            for (var at = type; at != null; at = at.BaseType)
            {
                var name = at.FullName;

                if (name == null)
                {
                    continue;
                }

                foreach (var root in Roots)
                {
                    if (name == root)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 이 타입이 보이는 요소일 때, 그것이 보여 주는 것을 읽을 멤버들을 <paramref name="into"/> 에 넣는다.
        /// </summary>
        /// <param name="into">이 타입에 대해 모으는 중인 멤버들.</param>
        /// <param name="taken">이미 그 이름으로 실린 것들. 게임의 필드가 같은 이름을 쥐고 있으면 두 번 싣지 않는다.</param>
        /// <param name="type">읽고 있는 객체 위의 구체 컴포넌트 타입.</param>
        /// <remarks>
        /// 필드가 아니라 프로퍼티다. <c>Text.text</c> 뒤의 필드는 <c>m_Text</c> 이고 그것은 Unity 버전마다 달라질 수 있는
        /// 이름이다. <see cref="LiveState"/> 의 읽기는 <c>Field</c> 가 <c>null</c> 이면 컴포넌트 자신에서
        /// <c>Member</c> 를 걷는 것으로 그것을 받는다.
        ///
        /// <c>Asked</c> 는 거짓이다. 어떤 근거도 이 값들을 청한 적이 없다. 읽을 수 있어서 싣는 것이고, 그 둘의 차이는 읽는
        /// 쪽의 물음이다.
        /// </remarks>
        internal static void Add(List<Watched> into, HashSet<string> taken, Type type)
        {
            if (type == null || !Is(type))
            {
                return;
            }

            foreach (var name in Names)
            {
                if (taken.Contains(name))
                {
                    continue;
                }

                PropertyInfo property;

                try
                {
                    property = type.GetProperty(name);
                }
                catch (Exception)
                {
                    // 리플렉션이 열지 못하는 프로퍼티 하나이지, 나머지를 잃을 이유가 아니다.
                    continue;
                }

                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                taken.Add(name);

                into.Add(new Watched
                {
                    Declaring = property.DeclaringType == null
                        ? type.FullName
                        : property.DeclaringType.FullName,
                    Member = name,
                    Property = null,
                    Type = property.PropertyType.FullName,
                    Static = false,
                    Field = null,
                    Owner = type,
                    Asked = false
                });
            }
        }
    }
}
