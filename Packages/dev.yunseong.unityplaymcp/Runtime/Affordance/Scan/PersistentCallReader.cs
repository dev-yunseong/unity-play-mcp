using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Artel.Affordances.Scan
{
    /// <summary>인스펙터가 부르라고 들은 메서드 하나와, 그것이 무엇에 매달려 있는지.</summary>
    internal struct PersistentCall
    {
        internal string Event;
        internal string TargetType;
        internal string TargetPath;
        internal string Method;
    }

    /// <summary>
    /// 디자이너가 인스펙터에서 한 배선을 읽는다.
    /// </summary>
    /// <remarks>
    /// 이것이 컴파일된 코드가 알 수 없는 절반이다. 버튼 핸들러의 본문은 무슨 일이 일어나는지를 말하지만, 그 안의 무엇도
    /// 어느 버튼인지, 애초에 버튼이 있기는 한지를 말하지 않는다. 그 연결은 직렬화된 persistent call 로 씬에 살고, 그 둘을
    /// 잇는 것이 어셈블리만 읽는 대신 런타임에 스캔하는 이유 전부다.
    ///
    /// 보이는 것은 persistent call 뿐이다. 코드에서 <c>AddListener</c> 로 더한 리스너는 직렬화되지 않아 셀 수도 없고
    /// 이름을 댈 수는 더더욱 없다 — 그 공백은 감추지 않고 보고한다.
    /// </remarks>
    internal static class PersistentCallReader
    {
        private const BindingFlags Fields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>이벤트 필드를 찾아 컴포넌트의 기반 타입을 얼마나 거슬러 오르는지.</summary>
        private const int MaxDepth = 16;

        private static readonly Dictionary<Type, FieldInfo[]> Known = new Dictionary<Type, FieldInfo[]>();

        internal static void Read(Component component, List<PersistentCall> into)
        {
            foreach (var field in EventFieldsOf(component.GetType()))
            {
                UnityEventBase wiring;

                try
                {
                    wiring = field.GetValue(component) as UnityEventBase;
                }
                catch (Exception)
                {
                    continue;
                }

                if (wiring == null)
                {
                    continue;
                }

                Read(field.Name, wiring, into);
            }
        }

        private static void Read(string name, UnityEventBase wiring, List<PersistentCall> into)
        {
            int count;

            try
            {
                count = wiring.GetPersistentEventCount();
            }
            catch (Exception)
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                try
                {
                    var target = wiring.GetPersistentTarget(index);

                    into.Add(new PersistentCall
                    {
                        Event = name,
                        TargetType = target == null ? null : target.GetType().FullName,
                        TargetPath = target is Component component ? ScenePath.Of(component.transform) : null,
                        Method = wiring.GetPersistentMethodName(index)
                    });
                }
                catch (Exception)
                {
                    // 읽을 수 없는 항목 하나이지, 그 이벤트의 나머지를 버릴 이유가 아니다.
                }
            }
        }

        /// <summary>
        /// 타입 위의 이벤트 모양 필드들. 엔진이 선언하는 private 인 것까지.
        /// </summary>
        /// <remarks>
        /// Button 은 <c>onClick</c> 을 프로퍼티로 내놓고, 그 뒤의 직렬화된 상태는 그것을 선언하는 클래스의 private 필드다.
        /// 리플렉션에는 비공개 멤버를 보라고 일러 줘야 하고 기반 타입도 스스로 걸어야 한다. 파생 타입에 비공개 멤버를 청해도
        /// 그 부모가 선언한 것에는 닿지 않기 때문이다.
        /// </remarks>
        private static FieldInfo[] EventFieldsOf(Type type)
        {
            if (Known.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var found = new List<FieldInfo>();
            var current = type;

            for (var depth = 0; depth < MaxDepth && current != null; depth++)
            {
                try
                {
                    foreach (var field in current.GetFields(Fields))
                    {
                        if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        {
                            found.Add(field);
                        }
                    }
                }
                catch (Exception)
                {
                    break;
                }

                current = current.BaseType;
            }

            var fields = found.ToArray();
            Known[type] = fields;
            return fields;
        }

        internal static void Forget()
        {
            Known.Clear();
        }
    }
}
