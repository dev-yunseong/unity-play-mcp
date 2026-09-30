using System.Collections.Generic;
using Mono.Cecil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// Unity 의 흔한 싱글턴 패턴을 알아본다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// void Awake() {
    ///     if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); }
    ///     else Destroy(gameObject);
    /// }
    /// </code>
    ///
    /// 이 패턴은 조건부 파괴와 상태 변경이라 후보 조건에 걸리지만 게임 동작이 아니다.
    ///
    /// 기록은 버리지 않고 인식 결과를 적어 남긴다. 인식된 것과 발견되지 않은 것을 읽는 쪽이 구분해야 한다.
    /// </remarks>
    internal static class SingletonPlumbing
    {
        /// <summary>이 경우가 인스턴스 하나를 유지하는 일만 하는지.</summary>
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
        /// 시작 콜백에서만 인정한다.
        /// </summary>
        /// <remarks>
        /// 다른 곳의 같은 효과는 실제 게임 동작이다. 예를 들어 획득 시 자신을 파괴하는 수집품이 그렇다.
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

            // 자기 객체를 파괴할 때만 이 패턴이다. receiver 를 읽지 못한 어셈블리는 아직 옛 표기
            // (`Component.gameObject` 등)로 떨어지므로 둘 다 받는다.
            return outcome.Target == "this.gameObject" ||
                   outcome.Target == "this" ||
                   outcome.Target == "Component.gameObject" ||
                   outcome.Target == "Object.gameObject" ||
                   outcome.Target == "Component.this";
        }

        /// <summary>
        /// 자기 타입의 static 필드에 대한 쓰기인지 본다.
        /// </summary>
        /// <remarks>
        /// 필드 이름(<c>instance</c>, <c>_instance</c>, <c>current</c> 등)은 프로젝트마다 달라 타입으로 검사한다.
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
