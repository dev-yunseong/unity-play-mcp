using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 메서드가 나중에 자신을 호출할 델리게이트에 등록되는 지점.
    /// </summary>
    /// <remarks>
    /// 이벤트 채널(ScriptableObject 애셋)로 발행자와 구독자가 이어지는 게임은 호출 그래프만으로 따라갈 수
    /// 없다.
    ///
    /// <c>ldftn</c> 에서 시작해 값을 앞으로 따라가 <c>add_</c> 접근자, <c>AddListener</c>, 델리게이트 필드
    /// 저장 중 무엇에 붙는지 찾는다.
    ///
    /// 어느 구독자가 어느 발행자를 듣는지는 씬의 직렬화 필드가 정하므로 여기서는 채널 타입만 적는다.
    /// </remarks>
    internal static class Subscriptions
    {
        /// <summary>델리게이트를 앞으로 따라가는 최대 명령어 수.</summary>
        private const int Reach = 8;

        internal static void ReadInto(BasicBlock block, ModuleDefinition module, List<Subscription> found)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var subscription = ReadAt(instruction, block, module);

                if (subscription != null)
                {
                    found.Add(subscription);
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }
        }

        private static Subscription ReadAt(Instruction instruction, BasicBlock block, ModuleDefinition module)
        {
            if (instruction.OpCode.Code != Code.Ldftn && instruction.OpCode.Code != Code.Ldvirtftn)
            {
                return null;
            }

            var handler = Resolve(instruction.Operand as MethodReference);

            if (handler == null || handler.Module != module)
            {
                // 다른 모듈(엔진 등)의 메서드는 게임 코드가 아니다.
                return null;
            }

            var attach = Attachment(instruction, block);

            if (attach == null)
            {
                return null;
            }

            var subscription = new Subscription
            {
                Handler = handler.FullName,
                HandlerId = MethodIdentity.Of(handler),
                Offset = instruction.Offset
            };

            if (attach.OpCode.Code == Code.Stfld || attach.OpCode.Code == Code.Stsfld)
            {
                var field = attach.Operand as FieldReference;

                if (field == null)
                {
                    return null;
                }

                // 필드 타입(UnityAction 등)이 아니라 선언 타입을 쓴다. 발행자가 부르는 Raise 도 그 타입에 있어
                // 구독자와 발행자를 이을 수 있다.
                subscription.Channel = IlReading.FieldName(field);
                subscription.ChannelType = field.DeclaringType?.FullName;
                subscription.Member = field.Name;
                return subscription;
            }

            var accessor = attach.Operand as MethodReference;

            if (accessor == null || !IsAttaching(accessor.Name))
            {
                return null;
            }

            subscription.Channel = IlReading.Receiver(accessor, attach, block.First);
            subscription.ChannelType = accessor.DeclaringType?.FullName;
            subscription.Member = accessor.Name.StartsWith("add_", System.StringComparison.Ordinal)
                ? accessor.Name.Substring(4)
                : accessor.Name;

            return subscription;
        }

        /// <summary>
        /// 델리게이트를 넘겨받는 명령어.
        /// </summary>
        /// <remarks>
        /// 그 사이에는 델리게이트 생성, <c>Delegate.Combine</c>, 캐스팅만 허용한다. 그 밖의 명령어를 지나치면
        /// 붙은 적 없는 채널을 가리킬 수 있어 null 을 돌려준다.
        /// </remarks>
        private static Instruction Attachment(Instruction from, BasicBlock block)
        {
            var at = from.Next;

            for (var step = 0; step < Reach && at != null; step++)
            {
                switch (at.OpCode.Code)
                {
                    case Code.Nop:
                    case Code.Newobj:
                    case Code.Castclass:
                        break;

                    case Code.Stfld:
                    case Code.Stsfld:
                        return at;

                    case Code.Call:
                    case Code.Callvirt:
                        // Delegate.Combine 은 필드 += 의 일부다. 그 밖의 호출이 구독 대상이다.
                        if (!IsCombining(at.Operand as MethodReference))
                        {
                            return at;
                        }

                        break;

                    default:
                        return null;
                }

                if (at == block.Last)
                {
                    return null;
                }

                at = at.Next;
            }

            return null;
        }

        private static MethodDefinition Resolve(MethodReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static bool IsCombining(MethodReference method)
        {
            return method != null &&
                   method.DeclaringType?.FullName == "System.Delegate" &&
                   (method.Name == "Combine" || method.Name == "Remove");
        }

        private static bool IsAttaching(string name)
        {
            return name != null &&
                   (name.StartsWith("add_", System.StringComparison.Ordinal) || name == "AddListener");
        }
    }
}
