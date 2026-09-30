using System.Collections.Generic;
using Mono.Cecil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 피호출자 쪽 용어를 호출 지점의 호출자 용어로 옮긴 대응표.
    /// </summary>
    /// <remarks>
    /// 피호출자의 조건은 자기 객체와 매개변수에 대한 것이라 그대로는 호출자 쪽에서 뜻이 없다. 호출 지점에는
    /// 수신 객체와 넘긴 인자가 호출자의 식으로 적혀 있으므로 그것으로 옮긴다.
    ///
    /// 옮기지 못하는 항이 하나라도 있는 조건은 통째로 내놓지 않는다. 반만 옮긴 조건은 서로 다른 두 객체에 대한
    /// 내용을 한 객체의 것처럼 읽히게 한다.
    /// </remarks>
    internal sealed class Binding
    {
        /// <summary>피호출자의 타입 이름. <c>this</c> 에 대한 항의 머리에 온다.</summary>
        internal string Owner;

        /// <summary>호출자가 이 메서드를 부른 수신 객체의 식.</summary>
        internal string Receiver;

        internal string ReceiverWhere;

        /// <summary>매개변수 이름에서 그 자리에 넘어간 인자 식으로의 대응.</summary>
        internal Dictionary<string, string> Passed;

        internal Dictionary<string, string> PassedWhere;

        internal bool Anything => Receiver != null || (Passed != null && Passed.Count > 0);

        /// <summary>인자를 그것이 채운 매개변수의 이름에 대응시킨다.</summary>
        internal static Binding Of(
            MethodDefinition callee, string receiver, string receiverWhere,
            string[] args, string[] argWhere)
        {
            var binding = new Binding
            {
                Owner = callee?.DeclaringType?.Name,
                Receiver = receiver,
                ReceiverWhere = receiverWhere
            };

            if (callee == null || args == null)
            {
                return binding;
            }

            binding.Passed = new Dictionary<string, string>(System.StringComparer.Ordinal);
            binding.PassedWhere = new Dictionary<string, string>(System.StringComparer.Ordinal);

            for (var index = 0; index < callee.Parameters.Count && index < args.Length; index++)
            {
                var name = callee.Parameters[index].Name;

                // 읽을 수 없는 인자이거나 이름 없는 매개변수는 빼 둔다. 그러면 그 항이 옮길 수 없는 채로 남아 조건 전체가
                // 거절된다.
                if (string.IsNullOrEmpty(name) || args[index] == null)
                {
                    continue;
                }

                binding.Passed[name] = args[index];
                binding.PassedWhere[name] = argWhere != null && index < argWhere.Length
                    ? argWhere[index]
                    : null;
            }

            return binding;
        }
    }
}
