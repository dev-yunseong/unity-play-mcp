using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 진입점들이 이 어셈블리 안에서 닿을 수 있는 모든 메서드.
    /// </summary>
    /// <remarks>
    /// 진입점은 시작점일 뿐이다. <c>Update</c> 가 읽는 키와 그 조건은 대개 그것이 부르는 private 헬퍼 안에 있으므로
    /// 진입점만 분석하면 답이 없다.
    ///
    /// 같은 모듈 안의 호출만 따라간다. 대상은 게임 코드이고, 엔진 참조를 해석하는 데 시간이 가장 많이 든다.
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
            /// 경로 어딘가에서 메서드가 호출되지 않고 delegate 로 건네졌을 때 참.
            /// </summary>
            /// <remarks>
            /// delegate 를 만든 조건은 그것이 실행되는 조건이 아니다. 그래서 그 엣지를 건너 조건을 나르지 않고, 조건이 없는
            /// 것이 "참이어야 할 것이 없다" 로 읽히지 않도록 엣지를 건넜다고 기록한다.
            /// </remarks>
            internal bool ThroughDelegate;

            /// <summary>
            /// 경로상 바로 앞 메서드에서 건네진 IL 오프셋. 없으면 -1.
            /// </summary>
            /// <remarks>
            /// 건네진 메서드는 호출 엣지가 없으므로 이 오프셋이 형제 효과들 사이의 순서를 정한다. 예: <c>WaitUntil</c>
            /// 술어가 앞뒤 효과 사이 어디서 기다리는지.
            /// </remarks>
            internal int HandedAt = -1;

            /// <summary><see cref="HandedAt"/> 이 <see cref="Path"/> 의 어느 걸음 안의 오프셋인지.</summary>
            internal int HandedIn = -1;

            /// <summary>건네진 메서드를 가져간 대상. 읽을 수 있을 때만.</summary>
            internal string HandedTo;
        }

        /// <summary>게임이 건넨 메서드와 건넨 자리.</summary>
        internal struct Handover
        {
            internal MethodDefinition Method;
            internal int Offset;

            /// <summary>가져간 대상. 읽을 수 있을 때만.</summary>
            internal string To;
        }

        /// <summary>
        /// 걷기를 멈추기 전까지 모으는 메서드 수.
        /// </summary>
        /// <remarks>
        /// 생성된 분배나 깊은 상호 재귀가 있는 어셈블리에서 닿는다. 걸리면 보고하므로 잘린 답이 작은 답으로 오해되지 않는다.
        /// </remarks>
        internal const int MaxMethods = 4000;
        internal const int MaxPathLength = 64;
        internal const int MaxInstructionsScanned = 200000;

        /// <summary>건네지기만 한 메서드를 주우러 되돌아가는 최대 횟수.</summary>
        /// <remarks>람다 안의 람다는 흔하지만 네 겹까지 가지는 않는다.</remarks>
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

            // 한 worklist 위의 두 단계다. 먼저 호출만 따라가고, 그다음 건네진 메서드에서 다시 호출을 따라간다. 두 경로로 모두
            // 닿는 메서드가 호출 쪽 결과를 갖게 한다.
            var rounds = 0;

            // 재귀 대신 worklist 를 쓴다. 생성된 코드의 호출 사슬은 스택을 넘칠 만큼 깊고, 그러면 에디터가 죽는다.
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

                    // 바로 넣지 않고 미룬다. 호출 경로는 호출 지점의 조건을 나르고 delegate 경로는 나르지 않으므로, 둘 다로 닿는
                    // 메서드는 호출 경로의 결과가 이겨야 한다.
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

                        // HandedIn 이 어느 걸음인지 가리키므로 뒤의 평범한 호출을 지나도 오프셋이 유효하다.
                        HandedAt = trace.HandedAt,
                        HandedIn = trace.HandedIn,
                        HandedTo = trace.HandedTo
                    });
                }
            }

            return reached;
        }

        /// <summary>미뤄 둔 trace 를 worklist 로 옮기고, 옮긴 것이 있었는지 돌려준다.</summary>
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
        /// 게임이 다른 코드가 부르도록 건네는 메서드들.
        /// </summary>
        /// <remarks>
        /// <c>ldftn</c> 으로 주소를 취한 메서드다. <c>WaitUntil</c> 에 넘긴 람다, 이벤트 핸들러, <c>Sort</c> 비교자처럼
        /// 엔진이나 라이브러리가 부르므로 호출을 따라가서는 닿지 않는다.
        ///
        /// <see cref="CalleeAt"/> 에 넣지 않는다. 이 엣지는 도달 가능성이지 호출이 아니며, 호출로 적으면 그 오프셋에서
        /// 일어나지 않는 호출을 주장하게 된다.
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
        /// 건네진 메서드를 가져간 대상.
        /// </summary>
        /// <remarks>
        /// 대상이 뜻을 정한다. <c>WaitUntil</c> 에 건넨 술어는 coroutine 을 멈춰 세우지만 콜백 목록에 건넨 것은 그렇지 않다.
        ///
        /// <c>ldftn</c> 뒤로 delegate 생성을 지나 처음 만나는 호출을 읽는다. 이름만 대고 해석은 읽는 쪽에 맡긴다.
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

                // delegate 자신의 생성자는 목적지가 아니다.
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

        /// <summary>건넨 지점에서 가져간 대상을 찾아보는 최대 명령어 수.</summary>
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
        /// 게임 코드에서는 호출되지 않는 coroutine 본문.
        /// </summary>
        /// <remarks>
        /// <c>yield</c> 메서드는 상태 기계를 만드는 생성기와 실제 본문인 <c>MoveNext</c> 로 컴파일된다. <c>MoveNext</c> 는
        /// 엔진이 부르므로 호출만 따라가면 모든 coroutine 본문을 놓친다.
        ///
        /// 생성기 안의 컴파일러 타입 <c>newobj</c> 를 엣지로 삼는다. <c>MoveNext</c> 가 있는 타입만 해당되어 람다의
        /// display class 와 구분된다.
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

        /// <summary>이 명령어가 부르는 게임 자신의 메서드.</summary>
        internal static MethodDefinition CalleeAt(Instruction instruction, ModuleDefinition module)
        {
            if (instruction.OpCode.FlowControl != FlowControl.Call ||
                !(instruction.Operand is MethodReference reference))
            {
                return null;
            }

            // 해석 전에 검사한다. 대부분의 호출은 엔진으로 가고 하나하나 해석하면 비싸다.
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

            // scope 가 null 이면 읽고 있는 모듈 안을 가리킨다.
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
