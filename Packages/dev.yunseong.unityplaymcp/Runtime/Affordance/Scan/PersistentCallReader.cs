using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>인스펙터에 연결된 persistent call 하나와 그 대상.</summary>
    internal struct PersistentCall
    {
        internal string Event;
        internal string TargetType;
        internal string TargetPath;
        internal string Method;
    }

    /// <summary>
    /// 인스펙터에서 연결한 `wiring` 을 읽는다.
    /// </summary>
    /// <remarks>
    /// 어느 버튼이 어느 핸들러를 부르는지는 씬의 persistent call 에만 있어 코드 분석으로는 알 수 없다.
    /// <c>AddListener</c> 로 더한 리스너는 직렬화되지 않아 보이지 않으며, 이 한계는 gap 으로 보고한다.
    /// </remarks>
    internal static class PersistentCallReader
    {
        private const BindingFlags Fields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>이벤트 필드를 찾아 거슬러 오르는 기반 타입의 최대 깊이.</summary>
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
                    // 읽지 못한 항목만 건너뛴다.
                }
            }
        }

        /// <summary>
        /// 타입의 <c>UnityEventBase</c> 필드들. 엔진이 선언한 private 필드도 포함한다.
        /// </summary>
        /// <remarks>
        /// Button 의 <c>onClick</c> 은 선언 클래스의 private 필드에 저장된다. 파생 타입에서는 부모의 private 필드가
        /// 보이지 않으므로 기반 타입을 직접 거슬러 오른다.
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
