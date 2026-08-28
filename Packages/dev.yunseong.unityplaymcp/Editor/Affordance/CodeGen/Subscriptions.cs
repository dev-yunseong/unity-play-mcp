using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 어떤 메서드가 나중에 자신을 부를 무언가에 걸린 자리.
    /// </summary>
    /// <remarks>
    /// 호출 그래프는 호출을 따라가는데, 이벤트 채널 위에 세워진 게임에는 호출이 거의 없다. 버튼은
    /// ScriptableObject 애셋에 발행하고, 실제 일을 하는 쪽은 전혀 다른 데서 같은 애셋을 구독해 두었다.
    /// 두 쪽은 서로의 이름을 한 번도 부르지 않는다. Chop Chop 에서 실측했을 때 이것은 부족분이 아니라
    /// 결과 전체였다 — 근거 기록 1,468건에 명세를 쓸 수 있는 것은 하나도 없었다. 모든 버튼이 채널에서
    /// 끝났기 때문이다.
    ///
    /// 구독 자체는 IL 안에 있고 놓칠 수 없다: 메서드의 주소를 취하는 것이 <c>ldftn</c> 이고, 그럴 이유는
    /// 정확히 하나뿐이다. 그것이 무엇에 붙었는지는 값을 앞으로 따라가 그것을 소비하는 것에 닿아서 안다 —
    /// <c>add_</c> 접근자이거나, <c>AddListener</c> 이거나, 델리게이트 필드로의 저장이다.
    ///
    /// 이것이 두 쪽을 이어 주지는 않는다. 이을 수 없다: 어느 구독자가 어느 발행자를 듣는지는 인스펙터
    /// 필드가 가리키는 애셋이 정하고, 그것은 코드가 아니라 씬에 있다. 여기 적히는 것은 양쪽 채널의
    /// 타입이고, 그것이 후보를 그 타입의 채널들로 좁히며, 나머지는 직렬화된 필드 읽기가 준다.
    /// </remarks>
    internal static class Subscriptions
    {
        /// <summary>델리게이트를 포기하기 전까지 앞으로 몇 명령어나 따라가는지.</summary>
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
                // 엔진 코드 위의 델리게이트. 게임이 쓴 것이 아니고 게임더러 돌리라고 할 수도 없다.
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

                // 필드를 선언하는 타입이지 필드 자신의 타입이 아니다. 필드형 이벤트는 델리게이트 필드로
                // 컴파일되므로 그 타입은 UnityAction 이다 — 맞는 말이고 쓸모없는 말이다. 구독자와 발행자를 만나게
                // 하는 것은 이벤트가 속한 타입이고, 그것은 발행자가 부른 Raise 를 선언하는 타입이기도 하다.
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
        /// 메서드의 주소를 취하는 것과 그것을 건네는 것 사이에 있는 것은 포장뿐이다: 델리게이트가 만들어지고,
        /// 때로는 이미 있던 것과 합쳐지고, 때로는 제 타입으로 다시 캐스팅된다. 그 밖의 것은 이 값이 여기서
        /// 따라갈 수 없는 데로 갔다는 뜻이고, 잘못 따라가면 그것이 붙은 적 없는 채널의 이름을 대게 된다.
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
                        // Delegate.Combine 은 필드에 대한 += 한가운데 앉아 있다. 그 밖의 것은 구독 대상 그 자체다.
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
