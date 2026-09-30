using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>컴포넌트의 인스펙터 필드가 가리키는 객체 하나.</summary>
    internal struct Reference
    {
        internal string Field;
        internal string Type;
        internal string Name;

        /// <summary>
        /// 두 필드가 같은 객체를 가리키는지 비교하는 조인 키.
        /// </summary>
        /// <remarks>
        /// 인스턴스 id 라 그 실행 안에서만 뜻이 있다. 같은 이벤트 채널 애셋을 쓰는 behaviour 들을 이 값으로 잇는다.
        /// </remarks>
        internal int Id;

        /// <summary>씬 객체일 때 그 경로.</summary>
        internal string Path;

        /// <summary>
        /// 어느 씬에도 없으면 true. 프리팹이거나 애셋이다.
        /// </summary>
        /// <remarks>
        /// 프리팹 루트의 경로는 씬 루트 객체의 경로와 구분되지 않으므로 명시한다. 테스트가 찾아갈 수 있는 것은 씬 객체뿐이다.
        /// </remarks>
        internal bool Asset;

        /// <summary>
        /// 참조된 프리팹이 나르는 컴포넌트 타입들.
        /// </summary>
        /// <remarks>
        /// 프리팹에만 있는 타입이 죽은 코드인지, 아직 인스턴스화되지 않았을 뿐인지 가리는 데 쓴다.
        /// </remarks>
        internal List<string> Carries;

        /// <summary>
        /// <see cref="SerializedReferences.Trace"/> 가 따라갈 객체. 리포트에는 쓰지 않는다.
        /// </summary>
        internal UnityEngine.Object Held;
    }

    /// <summary>
    /// 컴포넌트에 직렬화된 객체 참조를 읽는다.
    /// </summary>
    /// <remarks>
    /// 코드는 어느 채널 타입을 쓰는지만, 씬은 어느 애셋인지만 알므로 인스펙터 참조로 둘을 잇는다.
    /// 숫자나 문자열 값은 `wiring` 이 아니라 게임 데이터이고 리포트를 키우므로 읽지 않는다.
    /// </remarks>
    internal static class SerializedReferences
    {
        private const int MaxReferencesPerComponent = 32;
        private const int MaxElementsPerCollection = 16;

        /// <summary>프리팹 하나에서 읽는 서로 다른 컴포넌트 타입의 최대 수.</summary>
        private const int MaxCarriedTypes = 16;

        /// <summary>프리팹별 컴포넌트 타입 캐시.</summary>
        private static readonly Dictionary<int, List<string>> CarriedByPrefab =
            new Dictionary<int, List<string>>();

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, FieldInfo[]> FieldsByType =
            new Dictionary<Type, FieldInfo[]>();

        internal static void Read(Component component, List<Reference> found)
        {
            ReadInto(component, found);
        }

        private static void ReadInto(UnityEngine.Object holder, List<Reference> found)
        {
            if (holder == null)
            {
                return;
            }

            foreach (var field in FieldsOf(holder.GetType()))
            {
                if (found.Count >= MaxReferencesPerComponent)
                {
                    return;
                }

                object value;

                try
                {
                    value = field.GetValue(holder);
                }
                catch (Exception)
                {
                    // 타입 로드에 실패한 필드는 건너뛴다.
                    continue;
                }

                if (value is UnityEngine.Object single)
                {
                    Add(found, field.Name, single);
                    continue;
                }

                if (value is IEnumerable many && !(value is string))
                {
                    var taken = 0;

                    foreach (var element in many)
                    {
                        if (taken >= MaxElementsPerCollection || found.Count >= MaxReferencesPerComponent)
                        {
                            break;
                        }

                        if (element is UnityEngine.Object member)
                        {
                            Add(found, field.Name, member);
                            taken++;
                        }
                    }
                }
            }
        }

        private static void Add(List<Reference> found, string field, UnityEngine.Object value)
        {
            // 파괴된 객체와 인스펙터의 빈 슬롯은 Unity 의 == null 로 걸러진다.
            if (value == null)
            {
                return;
            }

            var reference = new Reference
            {
                Field = field,
                Type = value.GetType().FullName,
                Name = value.name,
                Id = value.GetInstanceID(),
                Held = value
            };

            var subject = value as GameObject ?? (value as Component)?.gameObject;

            if (subject == null)
            {
                // 스프라이트, 클립, ScriptableObject 같은 애셋.
                reference.Asset = true;
                found.Add(reference);
                return;
            }

            if (subject.scene.IsValid())
            {
                reference.Path = ScenePath.Of(subject.transform);
            }
            else
            {
                // 씬이 없으면 프리팹이다. 경로는 씬 루트와 구분되지 않으므로 쓰지 않는다.
                reference.Asset = true;
                reference.Carries = CarriedBy(subject);
            }

            found.Add(reference);
        }

        /// <summary>
        /// 애셋 참조를 몇 단계 따라가 결국 인스턴스화될 프리팹을 찾는다.
        /// </summary>
        /// <remarks>
        /// 프리팹은 <c>ScriptableObject</c> 를 거쳐 참조되는 일이 많다.
        /// 찾은 프리팹은 중간 애셋이 아니라 씬 안의 출발 필드에 귀속한다. 따라갈 수 있는 것이 그 필드이기 때문이다.
        /// <see cref="MaxTraceDepth"/> 와 <see cref="MaxTraced"/> 로 그래프 탐색을 제한한다.
        /// </remarks>
        internal static void Trace(UnityEngine.Object from, string ownerType, string field)
        {
            var seen = new HashSet<int>();
            Follow(from, ownerType, field, 0, seen);
        }

        private const int MaxTraceDepth = 2;
        private const int MaxTraced = 64;

        private static void Follow(
            UnityEngine.Object value, string ownerType, string field, int depth, HashSet<int> seen)
        {
            if (value == null || seen.Count >= MaxTraced || !seen.Add(value.GetInstanceID()))
            {
                return;
            }

            if (depth > MaxTraceDepth)
            {
                // 기록하지 않으면 createdBy 가 비어 죽은 코드로 읽힌다.
                // 깊이 제한은 그래프를 더 걷는 비용을 막는 것이므로 이 프리팹의 컴포넌트는 읽어 `cut` 항목으로 남긴다.
                var unread = value as GameObject ?? (value as Component)?.gameObject;

                if (unread != null && !unread.scene.IsValid())
                {
                    foreach (var carried in CarriedBy(unread))
                    {
                        AffordanceReport.CreatesCut(
                            carried, ownerType, field, unread.name, unread.GetInstanceID(), "depth");
                    }
                }

                return;
            }

            var subject = value as GameObject ?? (value as Component)?.gameObject;

            if (subject != null)
            {
                if (subject.scene.IsValid())
                {
                    // 이미 씬에 있는 객체는 생성 대상이 아니다.
                    return;
                }

                foreach (var carried in CarriedBy(subject))
                {
                    AffordanceReport.Creates(carried, ownerType, field, subject.name, subject.GetInstanceID());
                }

                // 프리팹의 컴포넌트가 다른 프리팹을 참조할 수 있다(예: 풀).
                foreach (var component in Components(subject))
                {
                    Onward(component, ownerType, field, depth, seen);
                }

                return;
            }

            // ScriptableObject 등 애셋의 필드도 읽는다. 간접 참조된 프리팹이 여기 있다.
            Onward(value, ownerType, field, depth, seen);
        }

        private static void Onward(
            UnityEngine.Object holder, string ownerType, string field, int depth, HashSet<int> seen)
        {
            var further = new List<UnityEngine.Object>();

            try
            {
                foreach (var slot in FieldsOf(holder.GetType()))
                {
                    Gather(slot.GetValue(holder), further, 0);
                }
            }
            catch (Exception)
            {
                return;
            }

            foreach (var reference in further)
            {
                Follow(reference, ownerType, field, depth + 1, seen);
            }
        }

        /// <summary>
        /// 값 안에 중첩된 모든 객체 참조를 모은다.
        /// </summary>
        /// <remarks>
        /// 프리팹이 <c>List&lt;EnemyData&gt;</c> 같은 직렬화 구조체 안에 있으면 객체 필드만 읽어서는 찾지 못한다.
        /// 결과는 리포트에 직접 쓰지 않고 <c>createdBy</c> 등록에만 쓴다.
        /// </remarks>
        private static void Gather(object value, List<UnityEngine.Object> into, int depth)
        {
            if (value == null || depth > MaxNesting || into.Count >= MaxTraced)
            {
                return;
            }

            if (value is UnityEngine.Object held)
            {
                if (held != null)
                {
                    into.Add(held);
                }

                return;
            }

            if (value is string)
            {
                return;
            }

            if (value is IEnumerable many)
            {
                foreach (var element in many)
                {
                    Gather(element, into, depth + 1);
                }

                return;
            }

            var type = value.GetType();

            if (type.IsPrimitive || type.IsEnum)
            {
                return;
            }

            var space = type.Namespace;

            if (space != null &&
                (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                 space == "System" || space.StartsWith("System.", StringComparison.Ordinal)))
            {
                return;
            }

            try
            {
                foreach (var slot in FieldsOf(type))
                {
                    Gather(slot.GetValue(value), into, depth + 1);
                }
            }
            catch (Exception)
            {
                // 필드를 읽지 못하면 이 값의 나머지는 건너뛴다.
            }
        }

        /// <summary>객체 참조를 찾아 직렬화 값 안으로 내려가는 최대 깊이.</summary>
        private const int MaxNesting = 4;

        private static Component[] Components(GameObject subject)
        {
            try
            {
                return subject.GetComponentsInChildren<Component>(true);
            }
            catch (Exception)
            {
                return new Component[0];
            }
        }

        /// <summary>
        /// 프리팹과 그 자식에 있는 게임 컴포넌트 타입들. 엔진 타입은 뺀다.
        /// </summary>
        /// <remarks>
        /// behaviour 가 자식에 붙은 프리팹도 있으므로 자식까지 본다.
        /// </remarks>
        private static List<string> CarriedBy(GameObject prefab)
        {
            var id = prefab.GetInstanceID();

            if (CarriedByPrefab.TryGetValue(id, out var already))
            {
                return already;
            }

            var carried = new List<string>();

            try
            {
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    if (carried.Count >= MaxCarriedTypes)
                    {
                        // 목록 길이만으로는 잘렸는지 알 수 없으므로 gap 을 남긴다.
                        AffordanceReport.CarriedTruncated(prefab.name);
                        continue;
                    }

                    var type = component.GetType();
                    var space = type.Namespace;

                    if (space != null &&
                        (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    // 기반 클래스도 넣는다. 그러지 않으면 공유 규칙을 가진 기반 타입(예: Enemy)이 죽은 코드로 읽힌다.
                    for (var current = type; Walkable(current); current = current.BaseType)
                    {
                        var name = current.FullName;

                        if (name == null || carried.Contains(name))
                        {
                            continue;
                        }

                        if (carried.Count >= MaxCarriedTypes)
                        {
                            AffordanceReport.CarriedTruncated(prefab.name);
                            continue;
                        }

                        carried.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                carried.Clear();
            }

            CarriedByPrefab[id] = carried;
            return carried;
        }

        /// <summary>
        /// Unity 가 직렬화하는 필드들. 엔진 기반 타입에 닿으면 멈춘다.
        /// </summary>
        /// <remarks>
        /// 출력이 결정적이도록 이름순으로 정렬하고, 타입별로 캐시한다.
        /// </remarks>
        private static FieldInfo[] FieldsOf(Type type)
        {
            if (FieldsByType.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var fields = new List<FieldInfo>();
            var named = new HashSet<string>(StringComparer.Ordinal);

            for (var current = type; Walkable(current); current = current.BaseType)
            {
                foreach (var field in current.GetFields(Declared))
                {
                    // 같은 이름의 기반 필드는 파생 필드가 가리므로 먼저 만난 파생 쪽만 쓴다.
                    if (Serialized(field) && named.Add(field.Name))
                    {
                        fields.Add(field);
                    }
                }
            }

            fields.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            var answer = fields.ToArray();
            FieldsByType[type] = answer;
            return answer;
        }

        /// <summary>
        /// 게임 코드 타입인지. UnityEngine 네임스페이스에 닿으면 멈춘다.
        /// </summary>
        /// <remarks>
        /// <c>Button</c> 의 <c>m_TargetGraphic</c> 같은 엔진 필드는 게임의 `wiring` 이 아니고 리포트를 크게 키우므로
        /// 기반 클래스 이름이 아니라 네임스페이스로 엔진 타입을 거른다.
        /// </remarks>
        private static bool Walkable(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return false;
            }

            var space = type.Namespace;

            return space == null ||
                   (space != "UnityEngine" &&
                    !space.StartsWith("UnityEngine.", StringComparison.Ordinal));
        }

        private static bool Serialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>(true) != null;
        }

        internal static void Forget()
        {
            FieldsByType.Clear();
            CarriedByPrefab.Clear();
        }
    }
}
