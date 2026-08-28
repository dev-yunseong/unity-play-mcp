using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 피호출자가 제 말로 하는 것이, 그것을 부른 자리에서는 무엇이라 불리는가.
    /// </summary>
    /// <remarks>
    /// 피호출자의 조건은 피호출자의 객체와 그 매개변수에 대한 것이고, 둘 다 호출자의 용어 옆에서는 아무
    /// 뜻도 없다 — 그래서 둘을 합성하는 일은 하지 않고 거절해 왔다. 거절이 옳은 것은 하나를 다른 쪽의
    /// 말로 옮길 방법이 없는 동안뿐이다. 호출 지점에는 그 방법이 있다: 호출자가 그 메서드를 무엇에 대고
    /// 불렀는지와 무엇을 넘겼는지를 직접 썼고, 둘 다 호출자 자신의 용어로 된 식이다.
    ///
    /// 그래서 이것은 번역이지 추측이 아니다. 옮기지 못하는 것은 전부 거절하고, 어느 한 부분이라도 거절된
    /// 조건은 아예 내놓지 않는다 — 반만 번역된 문장은 실제로는 둘인 것을 한 객체의 진술처럼 읽히게 한다.
    /// </remarks>
    internal sealed class Binding
    {
        /// <summary>피호출자의 타입. 제 <c>this</c> 에 대한 모든 항의 머리에 그 이름이 온다.</summary>
        internal string Owner;

        /// <summary>호출자가 그것을 무엇에 대고 불렀는지, 그리고 그것이 누구의 것인지.</summary>
        internal string Receiver;

        internal string ReceiverWhere;

        /// <summary>매개변수 이름과 그 자리에 넘어간 것, 그리고 그것이 누구의 것인지.</summary>
        internal Dictionary<string, string> Passed;

        internal Dictionary<string, string> PassedWhere;

        internal bool Anything => Receiver != null || (Passed != null && Passed.Count > 0);

        /// <summary>인자를 그것이 채운 매개변수의 이름으로 부른다.</summary>
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

                // 아무도 읽을 수 없는 인자이거나, 짝지을 이름이 없는 매개변수다. 빼 둔다 — 그래야 그것에 대한
                // 항이 번역 불가로 남고 조건 전체가 거절된다.
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
