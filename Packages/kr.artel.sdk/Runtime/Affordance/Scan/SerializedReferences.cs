using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>컴포넌트의 인스펙터 필드가 무엇을 가리키는가.</summary>
    internal struct Reference
    {
        internal string Field;
        internal string Type;
        internal string Name;

        /// <summary>
        /// 두 컴포넌트 위의 두 필드를 같은 객체로 만드는 것.
        /// </summary>
        /// <remarks>
        /// 그 숫자 자체는 그것을 만들어낸 실행 밖에서 아무 뜻도 없는데, 그것이 바로 원하는 바다: 그것은 이음쇠 키이지 간직할
        /// 정체가 아니다. 같은 이벤트 채널 애셋을 쥔 두 behaviour 는 여기서 같은 것을 나르고, 리포트 전체에서 그 사실이 존재하는
        /// 자리는 거기뿐이다 — 코드는 어떤 타입의 채널이라고 말하고 씬은 어느 애셋이라고 말하며, 어느 쪽도 홀로 그것을 말하지
        /// 않는다.
        /// </remarks>
        internal int Id;

        /// <summary>씬 안의 무엇일 때, 씬의 어디인지.</summary>
        internal string Path;

        /// <summary>
        /// 어느 씬에도 없을 때 참 — 프리팹이거나 애셋이다.
        /// </summary>
        /// <remarks>
        /// 말해야만 한다. 예전에는 그 둘이 똑같아 보였기 때문이다. 프리팹의 루트 transform 은 부모가 없으므로 그것에 대해
        /// 만들어진 경로는 제 이름이었는데, 그것이 정확히 씬 루트 객체의 경로가 생긴 모습이다:
        /// <c>CardManager.cardPrefab -&gt; "Card"</c> 와 <c>MapMove.character -&gt; "wordHead"</c> 가 같은 모양이었고 그중
        /// 하나만이 테스트가 갈 수 있는 자리였다.
        /// </remarks>
        internal bool Asset;

        /// <summary>
        /// 참조된 프리팹이 나르는 컴포넌트 타입들.
        /// </summary>
        /// <remarks>
        /// "누가 이것을 만드는가" 에 대한 답이 이것이다. 프리팹 위에만 존재하는 타입은 무언가 그것을 인스턴스화하기 전까지
        /// 리포트에서 빠져 있고, 리포트는 그것이 아무도 그러지 않기 때문인지 — 죽은 코드 — 아니면 그 실행이 아직 거기 이르지
        /// 못했기 때문인지 말할 수 없었다. 씬 *안에 있는* 컴포넌트가 인스펙터 필드로 쥔 프리팹은 두 번째 경우이고, 그것이
        /// 드러나는 자리가 여기다.
        /// </remarks>
        internal List<string> Carries;

        /// <summary>
        /// 더 따라가기 위한 객체 그 자체. 리포트에는 결코 쓰지 않는다.
        /// </summary>
        /// <remarks>
        /// 리포트가 받는 것은 이름과 이음쇠 키다. 이것은 살아 있는 참조이고, 두 걸음 떨어져 쥐고 있는 프리팹을 찾을 수 있도록
        /// 하기 위해서만 존재한다. 일부러 쓰인 형태에서 뺀다 — 이 실행 밖의 무엇도 그것을 쓸 수 없다.
        /// </remarks>
        internal UnityEngine.Object Held;
    }

    /// <summary>
    /// Unity 가 컴포넌트 위에 직렬화해 둔 객체 참조를 읽는다.
    /// </summary>
    /// <remarks>
    /// 분석은 코드를 읽고 스캔은 계층을 읽는데, 인스펙터 참조는 그 어느 쪽에도 속하지 않는 유일한 사실이다.
    /// <c>_teleportChannel.RaiseEvent()</c> 는 어느 채널인지 말하지 않은 채 코드 안에 있고, 애셋은 그것이 올라갔을 때 무슨
    /// 일이 일어나는지 말하지 않은 채 씬 안에 있다. Chop Chop 에서 실측하니 채널 타입 23 개가 근거 안에 발행자와 구독자를
    /// 둘 다 가지고 있었고 그중 하나도 실제 애셋과 짝지어질 수 없었다.
    ///
    /// 참조만 읽고 값은 읽지 않는다. 필드의 숫자나 문자열은 게임 자신의 데이터이고 아무 배선도 나르지 않는다. 그것을 읽으면
    /// 리포트가 게임 콘텐츠의 덤프가 되고, 덤프만 한 크기를 치르며, 플레이어가 무엇을 할 수 있는지에 대해서는 아무 말도
    /// 하지 않는다.
    /// </remarks>
    internal static class SerializedReferences
    {
        private const int MaxReferencesPerComponent = 32;
        private const int MaxElementsPerCollection = 16;

        /// <summary>프리팹 하나에서 서로 다른 컴포넌트 타입을 몇 개까지 읽는지.</summary>
        private const int MaxCarriedTypes = 16;

        /// <summary>각 프리팹이 무엇을 나르는지. 몇 개의 필드가 그것을 가리키든 한 번 알아낸다.</summary>
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
                    // 타입 로드에 실패한 필드. 읽을 수 없는 필드 하나가 그 컴포넌트 읽기를 멈출 이유는 아니다.
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
            // Unity 의 파괴된 객체는 여전히 살아 있는 참조이면서 null 과 같다고 비교된다. 인스펙터의 빈 슬롯도 여기에 같은 방식으로
            // 도착하고, 둘 다 테스트가 작용할 수 있는 무엇도 가리키지 않는다.
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
                // 스프라이트, 클립, ScriptableObject. 씬에 있는 일이 없고 걸어갈 수 있는 것을 나르지 않는다.
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
                // 씬이 없다는 것은 프리팹이라는 뜻이다. 그 경로는 아예 쓰지 않는다: 그것에 대해 만들 수 있는 문자열은 씬 루트의 것과
                // 구분되지 않고, 그것은 없느니만 못하다.
                reference.Asset = true;
                reference.Carries = CarriedBy(subject);
            }

            found.Add(reference);
        }

        /// <summary>
        /// 애셋을 한두 걸음 따라가 그것이 결국 씬에 무엇을 놓을지를 찾는다.
        /// </summary>
        /// <remarks>
        /// 프리팹은 곧바로 쥐고 있지 않은 일이 많다. 샘플 게임의 적들은 풀 컴포넌트가 가리키는 <c>ScriptableObject</c> 안에
        /// 살아서 사슬이 <c>EnemyPoolController.enemyDataContainer → EnemyData.prefab → Enemy</c> 이고, 컴포넌트 자신의
        /// 필드만 읽으면 그중 아무것도 찾지 못한다 — 그 때문에 리포트가 그 적들을 죽은 코드와 가리지 못했다.
        ///
        /// 중간의 연결 고리가 아니라 씬 안의 필드에 귀속시킨다. 사람이든 에이전트든 실제로 따라갈 수 있는 것이 그 필드이고,
        /// 중간 애셋의 이름을 대는 것은 그들이 밟을 수 없는 걸음에 대해 말해 주는 일이다.
        ///
        /// 두 걸음과 객체 예순넷 중 먼저 오는 쪽까지. ScriptableObject 는 그래프를 쥘 수 있고, 여기의 요점은 게임 콘텐츠를 걷는
        /// 것이 아니라 프리팹을 찾는 것이다.
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
                // 반환하는 이 순간에도 프리팹은 손에 있다. 여기서 놓으면 createdBy 가 비고, 소비자는
                // 그것을 죽은 코드로 읽는다 — 읽을 수 없어서 없는 것이 아니라 이미 본 것을 안 적는
                // 것이 된다.
                //
                // 이 프리팹이 무엇을 나르는지는 읽는다. 한계가 막으려는 것은 **그래프를 더 걷는
                // 비용**이고 컴포넌트 한 번 읽기는 거기 해당하지 않는다. 읽지 않으면 어느 타입의
                // createdBy 에 넣을지 알 수 없어, 프리팹 이름만 남기고 정작 살릴 타입을 못 살린다.
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
                    // 이미 씬 안에 있으므로 만들어져야 하는 무엇이 아니다.
                    return;
                }

                foreach (var carried in CarriedBy(subject))
                {
                    AffordanceReport.Creates(carried, ownerType, field, subject.name, subject.GetInstanceID());
                }

                // 프리팹 자신의 컴포넌트가 또 다른 프리팹을 쥘 수 있다 — 자기가 만들어낼 것을 쥔 풀.
                foreach (var component in Components(subject))
                {
                    Onward(component, ownerType, field, depth, seen);
                }

                return;
            }

            // ScriptableObject 이거나 그 밖의 애셋이다. 그 필드는 컴포넌트의 것과 같은 방식으로 읽는다. 간접적으로 쥐고 있는
            // 프리팹이 보관되는 자리가 거기이기 때문이다.
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
        /// 값 안의 모든 객체 참조. 게임이 아무리 깊이 중첩해 두었더라도.
        /// </summary>
        /// <remarks>
        /// 내놓는 참조만으로는 "누가 이것을 만드는가" 에 답할 수 없어서 쓴다. 샘플 게임은 적 프리팹을
        /// <c>List&lt;EnemyData&gt;</c> 에 두는데 <c>EnemyData</c> 는 평범한 직렬화 가능 구조체다 — 목록이 쥔 것은 객체가
        /// 아니라 구조체이므로, 객체인 필드만 읽어서는 아무것도 찾지 못했고 살아 있는 적 타입 다섯이 죽은 코드로 읽혔다.
        ///
        /// 이 걷기는 리포트에 닿지 않는다. 어떤 타입을 누가 만들지를 등록하려고 존재하고, 직렬화 가능 구조체에서 멈추는 것은
        /// 답을 한 걸음 앞두고 멈추는 일이다.
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
                // 읽히지 않는 필드 하나. 그 값의 나머지는 여전히 걸을 값이 있다.
            }
        }

        /// <summary>객체 참조를 찾아 직렬화 가능한 값을 얼마나 깊이 걷는지.</summary>
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
        /// 프리팹 위의, 게임 자신의 컴포넌트 타입들. 그 자식까지 포함해서.
        /// </summary>
        /// <remarks>
        /// 자식을 포함하는 것은 프리팹이 트리이고 behaviour 가 한 단계 아래에 있을 가능성도 그만큼 크기 때문이다 — animator 와
        /// collider 가 자식에 매달린 주문 프리팹처럼. 엔진 컴포넌트를 빼는 것은 그 필드를 빼는 것과 같은 이유다: 아무도 그것을
        /// 쓰지 않았다.
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
                        // 목록 길이만으로는 다 실린 것인지 잘린 것인지 알 수 없다. 잘렸다는 사실은
                        // 여기서만 알 수 있으므로 여기서 적는다.
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

                    // 기반 클래스도 함께. BossEnemy 를 나르는 프리팹은 인스턴스화되면 Enemy 이기도 하고 — 공유 규칙이 구워지는 타입이
                    // Enemy 이므로, 정확한 컴포넌트만 물으면 그 기반은 알려진 생성자가 없는 채로 남아 죽은 코드처럼 읽혔다.
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
        /// Unity 가 직렬화할 필드들. 그 타입에서 시작해 엔진 자신의 것이 시작되는 자리까지.
        /// </summary>
        /// <remarks>
        /// 같은 게임을 두 번 돌면 같은 바이트가 나오도록 이름으로 정렬하고, 한 씬이 적은 수의 타입의 인스턴스를 많이 쥐고 있으므로
        /// 캐시한다.
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
                    // 파생 클래스가 같은 이름의 기반 필드를 가릴 수 있다. 객체가 내놓는 것은 가장 파생된 쪽이고, 그것이 이미 취해진
                    // 그것이다.
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
        /// 게임 자신의 코드가 멈추는 자리에서 멈춘다.
        /// </summary>
        /// <remarks>
        /// 기반 클래스의 이름을 대는 대신 네임스페이스로 한다. <c>Button</c> 은 <c>MonoBehaviour</c> 만큼이나 엔진의 것이고 그
        /// <c>m_TargetGraphic</c> 은 엔진 배관이기 때문이다 — 맞는 말이고 누가 쓴 배선은 아니다. Chop Chop 에서 실측하니 그
        /// 필드들만으로 리포트의 3분의 1이었다.
        ///
        /// 엔진 타입에서 파생된 게임 타입은 제 필드를 전부 읽는다. 걷기는 사슬에서 엔진의 몫에 닿았을 때 멈추는데, 그 자리가
        /// 정확히 게임이 쓰기를 멈춘 자리다.
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
