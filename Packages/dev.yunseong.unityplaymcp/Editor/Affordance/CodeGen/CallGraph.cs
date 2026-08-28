using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 진입점들이 이 어셈블리 안에서 닿을 수 있는 모든 것.
    /// </summary>
    /// <remarks>
    /// 진입점은 뿌리이지 주제가 아니다. 진짜 behaviour 의 <c>Update</c> 는 호출 셋에 결정은 하나도 없기
    /// 십상이고, 그것이 읽는 키와 그 키를 지키는 조건들은 그것이 부르는 private 헬퍼 안에 앉아 있다 —
    /// 진입점을 고르는 어떤 규칙으로도 범위 밖이라 한 번도 들여다보이지 않는다. 뿌리에 대해서만 계산한
    /// control dependence 는 옳으면서 아무것도 답하지 못한다.
    ///
    /// 같은 모듈에 떨어지는 호출만 따라간다. 주제는 게임 자신의 코드이고 엔진 안에서 일어나는 일은
    /// 아니며, 그 참조들을 해석하는 데 걷기의 시간 대부분이 들어간다.
    /// </remarks>
    internal static class CallGraph
    {
        internal sealed class Trace
        {
            internal MethodDefinition Entry;
            internal MethodDefinition Method;
            internal List<MethodDefinition> Path;
            internal bool PathTruncated;

            /// <summary>
            /// 여기 오는 길 어딘가에서 게임이 메서드를 부르지 않고 건네준 적이 있을 때 참.
            /// </summary>
            /// <remarks>
            /// 이것이 중요한 이유는, 델리게이트가 *만들어진* 조건이 그것이 *도는* 조건이 아니기 때문이다.
            /// <c>OnEnable</c> 에서 붙인 핸들러는 조건 없이 만들어져 이벤트가 뜰 때마다 돌고,
            /// <c>WaitUntil</c> 에 건넨 술어는 예라고 답할 때까지 매 프레임 돈다. 둘 다 그것을 만든 자리가
            /// 아니므로 그 엣지를 건너 조건을 나르지 않는다 — 그리고 기록은 엣지를 건넜다고 말한다. 아무도 그
            /// 침묵을 "참이어야 했던 것이 없다" 로 읽지 않도록.
            /// </remarks>
            internal bool ThroughDelegate;

            /// <summary>
            /// 경로상 바로 앞 메서드의 어느 자리에서 건네졌는가, 또는 -1.
            /// </summary>
            /// <remarks>
            /// 건네는 그 자리에만 설정하고, 그 뒤의 평범한 호출을 지나 나르지 않는다 — 그 지점을 지나면 이 숫자는
            /// 경로가 더는 옆에서 끝나지 않는 메서드 안의 오프셋을 부르게 되고, 그것은 말하지 않느니만 못하다.
            ///
            /// 건네진 메서드는 호출 엣지가 없으므로 형제들 사이 어디에 속하는지 아무도 말하지 않았다. 그 효과는
            /// 그것을 건넨 메서드 안에 오프셋 순으로 앉아 있는데, 그 둘 사이에서 기다리는 술어는 그 사이에 놓일 수
            /// 없었다: 독자는 리포트가 세운 적 없는 순서를 추측하거나, 기다림을 빼놓거나 둘 중 하나였다. 오프셋이
            /// 필요한 것의 전부였고, 그것은 읽히고 나서 버려지고 있었다.
            /// </remarks>
            internal int HandedAt = -1;

            /// <summary>그 오프셋이 <see cref="Path"/> 의 어느 걸음 안의 오프셋인지.</summary>
            internal int HandedIn = -1;

            /// <summary>건네진 메서드를 무엇이 가져갔는가. 읽을 수 있을 때.</summary>
            internal string HandedTo;
        }

        /// <summary>게임이 건네준 메서드와, 그것을 건넨 자리.</summary>
        internal struct Handover
        {
            internal MethodDefinition Method;
            internal int Offset;

            /// <summary>무엇이 그것을 가져갔는가. 읽을 수 있을 때.</summary>
            internal string To;
        }

        /// <summary>
        /// 걷기를 포기하기 전까지 모으는 메서드 수.
        /// </summary>
        /// <remarks>
        /// 호출을 따라가는 일이 애초에 잘 끝날 리 없던 모양의 어셈블리에서 닿는다 — 생성된 분배, 깊은 상호
        /// 재귀. 걸렸을 때 개수를 보고하므로 잘린 답이 작은 답으로 오해되지 않는다.
        /// </remarks>
        internal const int MaxMethods = 4000;
        internal const int MaxPathLength = 64;
        internal const int MaxInstructionsScanned = 200000;

        /// <summary>건네지기만 한 것을 주우러 걷기가 몇 번이나 되돌아가는지.</summary>
        /// <remarks>람다 안의 람다는 평범하다. 그것이 넷 깊이로 이어지는 것은 아니다.</remarks>
        internal const int MaxDelegateRounds = 4;

        internal static List<Trace> Close(
            List<MethodDefinition> roots,
            ModuleDefinition module,
            out bool truncated)
        {
            truncated = false;

            var reached = new List<Trace>();
            var seen = new HashSet<string>();
            var pending = new Stack<Trace>();
            var deferred = new List<Trace>();
            var instructionsScanned = 0;

            foreach (var root in roots)
            {
                pending.Push(new Trace
                {
                    Entry = root,
                    Method = root,
                    Path = new List<MethodDefinition> { root }
                });
            }

            // 한 worklist 위의 두 라운드. 첫 라운드는 호출만 따라가고, 둘째는 건네지기만 한 것을 주워 거기서 다시
            // 호출을 따라간다. 이 순서가 두 갈래 모두로 닿을 수 있는 메서드가 둘 중 나은 진술을 갖게 한다.
            var rounds = 0;

            // 재귀가 아니라 worklist 다. 생성된 코드의 호출 사슬은 스택이 견디는 것보다 깊이 가고, 그 실패는
            // 예외가 아니라 죽은 에디터로 도착한다.
            while (pending.Count > 0 || (rounds++ < MaxDelegateRounds && Drain(deferred, pending)))
            {
                var trace = pending.Pop();
                var key = trace.Entry.MetadataToken.ToInt32() + ":" +
                          trace.Method.MetadataToken.ToInt32();

                if (!seen.Add(key))
                {
                    continue;
                }

                reached.Add(trace);

                if (reached.Count >= MaxMethods)
                {
                    truncated = true;
                    return reached;
                }

                var instructionCount = trace.Method.HasBody
                    ? trace.Method.Body.Instructions.Count
                    : 0;

                if (instructionsScanned > MaxInstructionsScanned - instructionCount)
                {
                    truncated = true;
                    return reached;
                }

                instructionsScanned += instructionCount;

                foreach (var handover in HandedOverBy(trace.Method, module))
                {
                    var handed = handover.Method;
                    var path = new List<MethodDefinition>(trace.Path);

                    if (path.Count < MaxPathLength)
                    {
                        path.Add(handed);
                    }

                    // 밀어 넣지 않고 나중을 위해 둔다. 한 메서드는 불리기도 하고 건네지기도 하는데, 불린 쪽 경로가 그것에
                    // 대한 더 나은 진술이다 — 그쪽은 호출 지점들의 조건을 나르고, 건네진 쪽은 아무것도 나르지 않는다.
                    // 한 스택 위에서 둘을 경주시키면 세 번에 한 번쯤 못한 답이 이겼다.
                    deferred.Add(new Trace
                    {
                        Entry = trace.Entry,
                        Method = handed,
                        Path = path,
                        PathTruncated = trace.PathTruncated || path.Count >= MaxPathLength,
                        ThroughDelegate = true,
                        HandedAt = handover.Offset,
                        HandedIn = path.Count - 2,
                        HandedTo = handover.To
                    });
                }

                foreach (var callee in CalleesOf(trace.Method, module))
                {
                    var path = new List<MethodDefinition>(trace.Path);
                    var pathTruncated = trace.PathTruncated;

                    if (path.Count < MaxPathLength)
                    {
                        path.Add(callee);
                    }
                    else
                    {
                        pathTruncated = true;
                    }

                    pending.Push(new Trace
                    {
                        Entry = trace.Entry,
                        Method = callee,
                        Path = path,
                        PathTruncated = pathTruncated,
                        ThroughDelegate = trace.ThroughDelegate,

                        // 이제 어느 본문에 속하는지를 말하므로 함께 나른다. 오프셋이 마지막 걸음을 뜻하기를 멈추고 어떤 걸음을
                        // 뜻하기 시작하므로, 건넨 뒤의 평범한 호출이 더는 그것을 거짓말로 만들지 않는다.
                        HandedAt = trace.HandedAt,
                        HandedIn = trace.HandedIn,
                        HandedTo = trace.HandedTo
                    });
                }
            }

            return reached;
        }

        /// <summary>치워 둔 것을 전부 worklist 로 옮기고, 그런 것이 있었는지 말한다.</summary>
        private static bool Drain(List<Trace> deferred, Stack<Trace> pending)
        {
            foreach (var trace in deferred)
            {
                pending.Push(trace);
            }

            var any = deferred.Count > 0;
            deferred.Clear();
            return any;
        }

        internal static IEnumerable<MethodDefinition> CalleesOf(MethodDefinition method, ModuleDefinition module)
        {
            if (!method.HasBody)
            {
                yield break;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                var callee = CalleeAt(instruction, module);

                if (callee != null)
                {
                    yield return callee;
                }

                var resumed = MachineAt(instruction, module);

                if (resumed != null)
                {
                    yield return resumed;
                }
            }
        }

        /// <summary>
        /// 게임이 다른 무언가더러 부르라고 건네주는 메서드들.
        /// </summary>
        /// <remarks>
        /// 메서드의 주소를 취하는 것이 <c>ldftn</c> 이고, 그럴 이유는 하나다: 다른 누군가가 그것을 돌릴
        /// 참이라는 것. <c>WaitUntil</c> 에 넘긴 람다, 이벤트에 더한 핸들러, <c>Sort</c> 에 준 비교 —
        /// 전부 게임이 쓴 코드이고 그 어느 것도 호출을 따라가서는 닿지 않는다. 호출이 엔진이나 라이브러리에서
        /// 이루어지기 때문이다.
        ///
        /// 샘플 게임에서 "다음 대사를 보려면 Space" 가 사는 자리가 여기다: 입력은
        /// <c>() =&gt; Input.GetKeyDown(Space)</c> 안에 있고, 그것을 만든 코루틴에서 호출을 따라가서는
        /// 한 번도 닿지 않았다.
        ///
        /// 일부러 <see cref="CalleeAt"/> 의 일부가 아니다. 그쪽은 "이 명령어가 무엇을 불렀는가" 에 답하고,
        /// 여기서의 답은 아무것도 아니다 — 이 엣지는 도달 가능성이지 호출이 아니며, 호출 엣지로 적으면 그
        /// 오프셋에서 일어나지 않는 호출을 주장하게 된다.
        /// </remarks>
        internal static IEnumerable<Handover> HandedOverBy(MethodDefinition method, ModuleDefinition module)
        {
            if (!method.HasBody)
            {
                yield break;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldftn && instruction.OpCode.Code != Code.Ldvirtftn)
                {
                    continue;
                }

                var handed = SafeResolve(instruction.Operand as MethodReference);

                if (handed != null && handed.HasBody && handed.Module == module)
                {
                    yield return new Handover
                    {
                        Method = handed,
                        Offset = instruction.Offset,
                        To = TakenBy(instruction)
                    };
                }
            }
        }

        /// <summary>
        /// 건네진 메서드가 무엇에게 주어졌는가.
        /// </summary>
        /// <remarks>
        /// 어디로 갔는지가 그것이 무엇을 뜻하는지를 정한다. <c>WaitUntil</c> 에 건넨 술어는 그것이 참이 될
        /// 때까지 멈춰 선 코루틴이므로 그 코루틴이 그 뒤에 하는 모든 것이 그것을 기다린다. 같은 술어를 콜백
        /// 목록에 건넨 것은 그런 뜻이 전혀 아니다. 리포트는 건네기가 어디서 일어났는지만 말하고 무엇이
        /// 가져갔는지는 말하지 않았으므로, 아무도 분기하지 않은 입력을 쥔 독자는 그 둘을 가릴 방법이 없었다 —
        /// 그리고 샘플 게임의 이야기 화면 전체가 <c>WaitUntil(() =&gt; GetKeyDown(Space))</c> 위에서 넘어간다.
        ///
        /// <c>ldftn</c> 에서 앞으로, 델리게이트 자신의 생성을 지나 읽는다. 그것과 그것을 원한 무언가 사이에
        /// 있는 것은 그뿐이다. 이름을 대되 해석하지는 않는다: 이것은 술어가 <c>UnityEngine.WaitUntil</c> 로
        /// 갔다고 말하고, 기다림이 무엇을 뜻하는지는 독자가 알 몫이다.
        /// </remarks>
        private static string TakenBy(Instruction handover)
        {
            var at = handover.Next;

            for (var step = 0; step < MaxHandoverLookahead && at != null; step++)
            {
                if (!(at.Operand is MethodReference taker))
                {
                    at = at.Next;
                    continue;
                }

                // 델리게이트 자신의 생성자는 포장이지 목적지가 아니다.
                if (at.OpCode.Code == Code.Newobj && taker.DeclaringType != null &&
                    IsDelegate(taker.DeclaringType))
                {
                    at = at.Next;
                    continue;
                }

                if (at.OpCode.Code == Code.Newobj || at.OpCode.Code == Code.Call ||
                    at.OpCode.Code == Code.Callvirt)
                {
                    var owner = taker.DeclaringType?.FullName;

                    return owner == null
                        ? null
                        : owner + "::" + taker.Name;
                }

                at = at.Next;
            }

            return null;
        }

        /// <summary>건네기 지점에서 그것을 가져간 것을 얼마나 멀리까지 찾는지.</summary>
        private const int MaxHandoverLookahead = 8;

        private static bool IsDelegate(TypeReference type)
        {
            var name = type.FullName;

            return name != null &&
                   (name.StartsWith("System.Func`", System.StringComparison.Ordinal) ||
                    name.StartsWith("System.Action", System.StringComparison.Ordinal) ||
                    name.StartsWith("System.Predicate`", System.StringComparison.Ordinal) ||
                    name.StartsWith("UnityEngine.Events.UnityAction", System.StringComparison.Ordinal));
        }

        /// <summary>
        /// 게임 안의 아무것도 부르지 않는, 코루틴의 본문.
        /// </summary>
        /// <remarks>
        /// <c>yield</c> 가 든 메서드는 둘로 컴파일된다: 상태 기계를 만들어 돌려주는 생성기와, 메서드가 실제로
        /// 한 일을 전부 담은 그 기계의 <c>MoveNext</c>. 게임은 생성기를 부른다. <c>MoveNext</c> 는 Unity 가
        /// 엔진에서 부르므로 게임 자신의 코드에서 호출을 따라가서는 거기 닿지 않고, 모든 코루틴의 본문 전체가
        /// 누락된다 — 샘플 게임에서는 캐스팅도, 웨이브 종료도, 대사도, 턴 넘기기도 전부 그 안에 있다.
        ///
        /// 엣지는 생성이다. 컴파일러 자신의 타입에 대한 <c>newobj</c> 가 그 기계가 존재하게 되는 순간이고,
        /// 그것은 생성기 안, 게임 자신의 코드 안, 볼 수 있는 자리에서 일어난다. <c>MoveNext</c> 를 가진
        /// 타입만 해당되며, 그것이 이터레이터와 람다의 display class 를 가른다.
        /// </remarks>
        internal static MethodDefinition MachineAt(Instruction instruction, ModuleDefinition module)
        {
            if (instruction.OpCode.Code != Code.Newobj ||
                !(instruction.Operand is MethodReference constructor))
            {
                return null;
            }

            var type = constructor.DeclaringType;

            if (type == null || !type.Name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            var definition = SafeResolveType(type);

            if (definition == null || definition.Module != module)
            {
                return null;
            }

            foreach (var method in definition.Methods)
            {
                if (method.Name == "MoveNext" && method.HasBody && method.Parameters.Count == 0)
                {
                    return method;
                }
            }

            return null;
        }

        private static TypeDefinition SafeResolveType(TypeReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>이 명령어가 부르는 메서드. 그것이 게임 자신의 것일 때.</summary>
        internal static MethodDefinition CalleeAt(Instruction instruction, ModuleDefinition module)
        {
            if (instruction.OpCode.FlowControl != FlowControl.Call ||
                !(instruction.Operand is MethodReference reference))
            {
                return null;
            }

            // 해석하기 전에 검사한다. behaviour 안의 거의 모든 호출은 엔진으로 들어가고, 그것을 알아내려고
            // 하나하나 해석하는 것은 비싼 방법이다.
            if (!IsSameModule(reference, module))
            {
                return null;
            }

            var definition = SafeResolve(reference);

            return definition != null && definition.HasBody && definition.Module == module
                ? definition
                : null;
        }

        private static bool IsSameModule(MethodReference reference, ModuleDefinition module)
        {
            var scope = reference.DeclaringType?.Scope;

            // scope 가 null 이면 그 참조는 읽고 있는 모듈 안의 무언가를 부르는 것이다.
            return scope == null || ReferenceEquals(scope, module);
        }

        private static MethodDefinition SafeResolve(MethodReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
