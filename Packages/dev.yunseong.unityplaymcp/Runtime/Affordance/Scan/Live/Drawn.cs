using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 플레이어가 화면에서 읽는 라벨, 그림, 슬라이더 같은 컴포넌트를 찾는다.
    /// </summary>
    /// <remarks>
    /// <c>UnityEngine.UI</c> 와 <c>TMPro</c> 는 Unity 어셈블리라 <c>evidence</c> 가 없어서 <see cref="Worth"/> 에 걸리지 않는다.
    /// 여기서 따로 잡지 않으면 <c>Text</c> 와 <c>Image</c> 가 pulse 에 실리지 않는다.
    ///
    /// <c>Slider</c> 는 <c>Graphic</c> 이 아니라 <c>Selectable</c> 이므로 둘 다 뿌리로 본다.
    ///
    /// uGUI 와 TextMeshPro 는 없을 수 있는 패키지라 이 어셈블리는 참조하지 않고 타입 이름으로 맞춘다
    /// (<see cref="Scan.SceneEvidenceScan"/> 와 같은 이유).
    ///
    /// 분류는 MCP server 가 한다. SDK 는 타입 이름을 <c>by[].on</c> 에 실을 뿐이라 목록을 고쳐도 Unity 재빌드가 필요 없다.
    /// </remarks>
    internal static class Drawn
    {
        /// <summary>이 셋 중 하나를 상속하면 화면에 무언가를 내놓는 컴포넌트로 본다.</summary>
        private static readonly string[] Roots =
        {
            "UnityEngine.UI.Graphic", "UnityEngine.UI.Selectable", "TMPro.TMP_Text"
        };

        /// <summary>
        /// 컴포넌트가 지금 보여 주는 값을 읽을 프로퍼티 이름.
        /// </summary>
        /// <remarks>
        /// <c>texture</c> 와 <c>interactable</c> 은 <c>RawImage</c> 와 <c>Button</c> 처럼 다른 멤버가 없는 컴포넌트를 위한
        /// 것이다. 멤버가 하나도 없으면 <c>by</c> 항목이 안 써져(<see cref="LiveState"/> 의 <c>count > 0</c>) 타입 이름이
        /// 나가지 않는다.
        ///
        /// <c>interactable</c> 은 컴포넌트 자체 값이다. <c>click</c> 이 쓰는 <c>Selectable.IsInteractable()</c> 은 부모
        /// <c>CanvasGroup</c> 까지 보지만 메서드라 여기서 읽을 수 없다.
        ///
        /// <c>color</c> 는 struct 라 <c>{"is":"UnityEngine.Color"}</c> 만 나가므로 싣지 않는다.
        /// </remarks>
        private static readonly string[] Names =
        {
            "text", "fillAmount", "value", "normalizedValue", "isOn", "texture", "interactable"
        };

        /// <summary>
        /// 캐시를 비우기 전까지 기억하는 객체 수.
        /// </summary>
        /// <remarks>
        /// 객체를 계속 만들고 부수는 게임에서 캐시가 끝없이 자라지 않게 한다(<see cref="Worth"/> 와 같다).
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, bool> Answered = new Dictionary<int, bool>();

        /// <summary>이 객체가 그런 컴포넌트를 하나라도 나르는가. 객체마다 한 번 답하고 기억한다.</summary>
        /// <remarks>
        /// 매 pulse 마다 컴포넌트를 훑지 않으려고 캐시한다. 객체의 컴포넌트 타입은 생성 시 정해진다고 가정한다
        /// (<see cref="LiveState"/> 의 offer 캐시와 같은 가정).
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
        /// 이 타입이 보이는 요소이면 그 표시 값을 읽을 멤버를 <paramref name="into"/> 에 넣는다.
        /// </summary>
        /// <param name="taken">이미 실린 이름. 게임 필드와 이름이 겹치면 두 번 싣지 않는다.</param>
        /// <remarks>
        /// 필드 이름(<c>m_Text</c> 등)은 Unity 버전마다 달라질 수 있어 프로퍼티를 읽는다. <c>Field</c> 가 <c>null</c> 이면
        /// <see cref="LiveState"/> 가 컴포넌트에서 <c>Member</c> 를 읽는다.
        ///
        /// 어떤 <c>evidence</c> 도 청하지 않은 값이므로 <c>Asked</c> 는 false 다.
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
                    // 한 프로퍼티를 못 열어도 나머지는 싣는다.
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
