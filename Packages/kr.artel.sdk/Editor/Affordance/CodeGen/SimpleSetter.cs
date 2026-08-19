using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 접근자가 필드 하나를 건드리는 것 말고는 아무것도 하지 않는 프로퍼티.
    /// </summary>
    /// <remarks>
    /// 프로퍼티를 거쳐 바뀐 상태는 보이지 않았다. 효과로 세는 것은 필드 저장과 엔진 setter 목록뿐이라,
    /// <c>controller.currentLife -= 1</c> 이라고 쓰는 게임은 목숨을 잃는 조건은 완벽하게 읽히면서 효과는
    /// 소리 하나뿐이었다. Trash Dash 에서 실측하니 그것이 목숨과 코인과 프리미엄 — 그 게임을 이루는 세
    /// 가지였다.
    ///
    /// 사소한 접근자만 본다. 그 제한이 설계의 전부다. 제 본문을 가진 setter 는 분석이 이미 읽는 메서드다:
    /// 같은 모듈 안의 호출이므로 제 기록이 제 효과를 나른다. 호출 지점을 쓰기로 *한 번 더* 세면 한 번의
    /// 변화가 두 번 보고되고, 그 둘을 합칠 것이 없으며, 독자는 그 둘이 같은 사건임을 알 방법이 없다.
    ///
    /// 프로퍼티가 아니라 필드의 이름을 붙인다. 그래야 쓰는 두 방식이 서로 일치한다. 클래스 안에서는
    /// 컴파일러가 필드 저장을 뱉고, 밖에서는 이 호출을 뱉는다. 그 둘은 하나의 사실이고 하나의 이름으로
    /// 도착해야 한다.
    /// </remarks>
    internal static class SimpleSetter
    {
        /// <summary>접근자가 사소한 것으로 세어지면서 담을 수 있는 명령어 수.</summary>
        /// <remarks>
        /// 저장은 셋에 return, 적재는 둘에 return 이다. 디버그 빌드가 채워 넣는 <c>nop</c> 을 받아들일 만큼
        /// 넉넉하고, 분기가 들어간 것은 아무것도 통과하지 못할 만큼 빡빡하다.
        /// </remarks>
        private const int MaxInstructions = 8;

        private static readonly Dictionary<string, FieldReference> Known =
            new Dictionary<string, FieldReference>(StringComparer.Ordinal);

        /// <summary>
        /// 사소한 접근자가 닿는 필드. 그 밖의 것이면 null.
        /// </summary>
        /// <remarks>
        /// 참조 자신의 이름으로 캐시한다. 프로퍼티는 여러 자리에서 불리고 그 사이에 답이 달라질 수 없기
        /// 때문이다.
        /// </remarks>
        internal static FieldReference FieldBehind(MethodReference method)
        {
            if (method == null || !IsAccessor(method.Name))
            {
                return null;
            }

            var key = method.FullName;

            if (Known.TryGetValue(key, out var already))
            {
                return already;
            }

            var found = Read(method);
            Known[key] = found;
            return found;
        }

        private static bool IsAccessor(string name)
        {
            return name.Length > 4 &&
                   (name.StartsWith("set_", StringComparison.Ordinal) ||
                    name.StartsWith("get_", StringComparison.Ordinal));
        }

        private static FieldReference Read(MethodReference method)
        {
            MethodDefinition definition;

            try
            {
                definition = method.Resolve();
            }
            catch (Exception)
            {
                return null;
            }

            if (definition == null || !definition.HasBody)
            {
                return null;
            }

            // 엔진 자신의 프로퍼티는 게임의 상태가 아니고, 그중 몇은 이미 이름으로 그것이 무엇인지 인식된다.
            var space = definition.DeclaringType?.Namespace;

            if (space != null &&
                (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal)))
            {
                return null;
            }

            var body = definition.Body;

            if (body.Instructions.Count > MaxInstructions)
            {
                return null;
            }

            FieldReference touched = null;

            foreach (var instruction in body.Instructions)
            {
                switch (instruction.OpCode.Code)
                {
                    case Code.Nop:
                    case Code.Ret:
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg:
                    case Code.Ldarg_S:
                    case Code.Stloc_0:
                    case Code.Ldloc_0:
                        continue;

                    case Code.Stfld:
                    case Code.Stsfld:
                    case Code.Ldfld:
                    case Code.Ldsfld:
                        if (touched != null)
                        {
                            // 필드가 둘이다. 그러니 이것은 프로퍼티의 이름을 쓴 필드 하나가 아니다.
                            return null;
                        }

                        touched = instruction.Operand as FieldReference;
                        continue;

                    default:
                        // 그 밖의 무엇이든 — 분기, 호출, 산술 — 접근자가 제 일을 한다는 뜻이고, 그 일은 그것이 쓰인
                        // 자리에서 읽힌다.
                        return null;
                }
            }

            return touched;
        }

        /// <summary>어셈블리 사이에서 비운다: 한 이름이 각각에서 다른 것을 뜻한다.</summary>
        internal static void Forget()
        {
            Known.Clear();
        }
    }
}
