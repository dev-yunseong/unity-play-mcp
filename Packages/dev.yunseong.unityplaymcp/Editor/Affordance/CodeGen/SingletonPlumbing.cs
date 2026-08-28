using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 모든 Unity 프로젝트가 쓰는 그 싱글턴을 알아본다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// void Awake() {
    ///     if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); }
    ///     else Destroy(gameObject);
    /// }
    /// </code>
    ///
    /// 이것은 객체를 파괴하고 조건에 따라 제 상태를 바꾸는 게임이라는 근거로 읽히는데, 그것이 바로 후보의
    /// 조건이다. 샘플 게임에 열둘이 있었고, 그 하나하나가 씬에 두 번 들어가면 무언가가 지워진다는 명세가
    /// 될 참이었다.
    ///
    /// 버리지는 않는다. 기록은 그것이 무엇인지를 적은 채로 남는다. "이것은 배관으로 인식됐다" 와 "이것은
    /// 발견된 적이 없다" 는 다른 일이고, 둘을 가리지 못하는 독자는 두 번째를 찾으러 나서기 때문이다.
    /// </remarks>
    internal static class SingletonPlumbing
    {
        /// <summary>이 경우가 하는 일이 제 인스턴스 하나를 살려 두는 것뿐인지.</summary>
        internal static bool Explains(MethodDefinition entry, List<Outcome> outcomes)
        {
            if (outcomes.Count == 0 || !IsStartup(entry?.Name))
            {
                return false;
            }

            var owner = entry.DeclaringType;

            foreach (var outcome in outcomes)
            {
                if (!IsSelfDestruction(outcome) && !IsInstanceField(outcome, owner))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 이 패턴이 사는 자리에서만.
        /// </summary>
        /// <remarks>
        /// 같은 두 효과를 다른 어디에 쓰면 그것은 게임이 실제로 하는 일이다 — 획득 시 스스로를 파괴하는
        /// 수집품이 정확히 이렇게 쓴다. 이것을 배관으로 만드는 것은, 그것이 객체가 올라올 때 아무도 아무것도
        /// 하기 전에 돈다는 점이다.
        /// </remarks>
        private static bool IsStartup(string name)
        {
            return name == "Awake" || name == "OnEnable";
        }

        private static bool IsSelfDestruction(Outcome outcome)
        {
            if (outcome.Kind != "destroy")
            {
                return false;
            }

            // 제 객체이고, 그것을 가져온 프로퍼티의 이름으로 불린다. 시작할 때 다른 것을 파괴하는 것은 이
            // 패턴이 아니다.
            //
            // `this.gameObject` 와 맨 `this` 는 reader 가 호출이 이루어진 객체에 이름을 붙이게 된 지금 하는
            // 말이고, 옛 표기도 남겨 둔다. 거기까지 읽히지 않는 어셈블리는 여전히 그쪽으로 물러서기 때문이다.
            return outcome.Target == "this.gameObject" ||
                   outcome.Target == "this" ||
                   outcome.Target == "Component.gameObject" ||
                   outcome.Target == "Object.gameObject" ||
                   outcome.Target == "Component.this";
        }

        /// <summary>
        /// 타입이 제 타입으로 가진 static 필드에 대한 쓰기 — 인스턴스 보관함이다.
        /// </summary>
        /// <remarks>
        /// 이름이 아니라 필드로 검사한다. <c>instance</c>, <c>Instance</c>, <c>_instance</c>, <c>current</c>
        /// 는 전부 같은 생각이고, 이름 목록은 다음 프로젝트에서 틀릴 목록이다.
        /// </remarks>
        private static bool IsInstanceField(Outcome outcome, TypeDefinition owner)
        {
            if (outcome.Kind != "write" || owner == null || outcome.Target == null)
            {
                return false;
            }

            var dot = outcome.Target.LastIndexOf('.');

            if (dot < 0)
            {
                return false;
            }

            var name = outcome.Target.Substring(dot + 1);

            foreach (var field in owner.Fields)
            {
                if (field.Name != name)
                {
                    continue;
                }

                return field.IsStatic && field.FieldType?.FullName == owner.FullName;
            }

            return false;
        }
    }
}
