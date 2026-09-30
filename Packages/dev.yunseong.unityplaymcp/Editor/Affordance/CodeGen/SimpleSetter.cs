using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 접근자가 필드 하나를 읽거나 쓰기만 하는 프로퍼티.
    /// </summary>
    /// <remarks>
    /// 이것이 없으면 <c>controller.currentLife -= 1</c> 같은 프로퍼티 경유 쓰기가 효과로 잡히지 않는다.
    ///
    /// 사소한 접근자만 본다. 본문이 있는 setter 는 분석이 따로 읽으므로, 호출 지점까지 쓰기로 세면 한 번의
    /// 변화가 두 번 보고된다.
    ///
    /// 프로퍼티가 아니라 필드 이름을 쓴다. 클래스 안의 필드 저장과 밖의 접근자 호출이 같은 이름이어야 한다.
    /// </remarks>
    internal static class SimpleSetter
    {
        /// <summary>사소한 접근자로 인정하는 최대 명령어 수.</summary>
        /// <remarks>
        /// 디버그 빌드의 <c>nop</c> 은 허용하고 분기가 들어간 본문은 걸러지는 크기다.
        /// </remarks>
        private const int MaxInstructions = 8;

        private static readonly Dictionary<string, FieldReference> Known =
            new Dictionary<string, FieldReference>(StringComparer.Ordinal);

        /// <summary>
        /// 사소한 접근자가 닿는 필드. 사소한 접근자가 아니면 null.
        /// </summary>
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

            // 엔진 프로퍼티는 게임 상태가 아니고, 일부는 이미 이름으로 인식된다.
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
                            // 필드가 둘이면 필드 하나를 감싼 프로퍼티가 아니다.
                            return null;
                        }

                        touched = instruction.Operand as FieldReference;
                        continue;

                    default:
                        // 분기, 호출, 산술이 있으면 접근자 본문을 분석이 따로 읽는다.
                        return null;
                }
            }

            return touched;
        }

        /// <summary>어셈블리마다 비운다. 같은 이름이 어셈블리마다 다른 것을 가리킬 수 있다.</summary>
        internal static void Forget()
        {
            Known.Clear();
        }
    }
}
