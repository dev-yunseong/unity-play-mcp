using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 그래프로 만든 메서드를 입력·선행조건·결과로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 결과에서 거슬러 읽는다. 결과가 있는 블록이 control dependent 한 결정들을 재귀적으로 따라가면 그 블록에
    /// 닿는 조건 전체가 된다. 그중 입력 검사가 입력이 되고 나머지가 선행 조건이 된다.
    ///
    /// 이 방식에서는 <c>A || B</c> 에 특별한 처리가 필요 없다. 두 검사가 모두 같은 결과를 다스린다.
    /// </remarks>
    internal static class VariantBuilder
    {
        private const string InputType = "UnityEngine.Input";

        /// <summary>이 패키지의 weaver 가 <c>UnityEngine.Input</c> 호출을 바꿔 넣는 타입.</summary>
        /// <remarks>
        /// weaver 와 이 분석 중 무엇이 먼저 도는지 Unity 가 보장하지 않으므로 두 이름을 모두 받는다. 멤버 이름은
        /// 같다. 하나만 보면 순서에 따라 입력을 전부 조용히 놓친다.
        /// </remarks>
        private const string ProxiedInputType = "UnityPlayMcp.VirtualInput";

        private const byte Unvisited = 0;
        private const byte Computing = 1;
        private const byte Settled = 2;

        internal static void Collect(
            MethodDefinition method,
            MethodDefinition entry,
            IReadOnlyList<MethodDefinition> callPath,
            bool callPathTruncated,
            ControlFlowGraph graph,
            ControlDependence dependence,
            List<Variant> variants,
            string triggerKind,
            Condition reachedBy,
            bool sameObject,
            Binding binding)
        {
            // 블록 조건은 메서드 전체에서 한 번만 계산해 재사용한다.
            var reached = new Condition[graph.Blocks.Count];
            var state = new byte[graph.Blocks.Count];

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                var outcomes = OutcomesIn(block, method);
                var calls = CallsIn(block, method.Module, graph.HasThis);

                var handles = new List<Subscription>();
                Subscriptions.ReadInto(block, method.Module, handles);

                // 분기 없이 입력 읽기 결과만 돌려주는 블록. `WaitUntil(() => Input.GetKeyDown(Space))` 의 술어가
                // 이 모양이라 분기 조건으로는 잡히지 않는다.
                //
                // 분기하는 블록의 입력은 이미 조건에 들어 있으므로 제외한다.
                var answered = outcomes.Count == 0 && calls.Count == 0 && handles.Count == 0 &&
                               block.Last?.OpCode.FlowControl != FlowControl.Cond_Branch
                    ? InputIn(block)
                    : null;

                if (answered == null &&
                    outcomes.Count == 0 && calls.Count == 0 && handles.Count == 0)
                {
                    continue;
                }

                var own = Reach(graph, dependence, block.Index, reached, state);
                var derived = !ReferenceEquals(method, entry);

                // 기준 객체가 다른 두 조건은 합치지 않는다. 피호출자의 `count > 0` 을 호출자 조건 옆에 두면 호출자의
                // `count` 로 읽힌다. 한쪽 조건이 Always 면 섞일 것이 없어 안전하다.
                //
                // 두 조건이 같은 객체 기준일 때만 합친다. 경로의 모든 호출이 호출자의 `this` 에 대한 것이고 양쪽 조건이
                // `this` 나 static 만 말할 때다. 추측으로 넓히면 다른 객체에 대한 조건을 만든다.
                //
                // `binding` 이 있으면 피호출자의 조건을 호출자 기준으로 바꿔 쓴다.
                var said = binding == null || sameObject ? null : own.ReadFrom(binding);

                if (said != null)
                {
                    own = said;
                }

                var joinable = (sameObject || said != null) &&
                               own.AboutSelfOnly() && reachedBy.AboutSelfOnly();

                var mixes = derived &&
                            !joinable &&
                            reachedBy.Kind != ConditionKind.Always &&
                            own.Kind != ConditionKind.Always;

                var composable = !mixes;

                // 섞일 때는 호출자 조건 중 입력만 가져온다. 입력은 객체에 매이지 않아 어디서나 뜻이 같다. 이것이 없으면
                // 키를 한 메서드에서 읽고 효과를 다른 메서드에서 내는 게임에서 입력이 사라진다.
                var carried = mixes ? reachedBy.InputsOnly() : reachedBy;

                var when = derived
                    ? Condition.Every(new[] { carried, own })
                    : own;

                var variant = new Variant
                {
                    Method = method.FullName,
                    MethodId = MethodIdentity.Of(method),
                    Entry = entry.FullName,
                    EntryId = MethodIdentity.Of(entry),
                    TriggerKind = triggerKind,
                    Owner = Owning(method.DeclaringType) ?? Owning(entry.DeclaringType),
                    When = when
                };

                foreach (var step in callPath)
                {
                    variant.CallPath.Add(step.FullName);
                }

                if (callPathTruncated)
                {
                    variant.AddGap("call-path-limit");
                }

                if (derived && joinable && reachedBy.Kind != ConditionKind.Always &&
                    own.Kind != ConditionKind.Always)
                {
                    // 합친 조건에서는 어느 부분이 호출자에서 왔는지 보이지 않으므로 표시한다.
                    variant.AddGap("composed-on-same-object");
                }

                if (derived && !composable)
                {
                    variant.AddGap("callee-condition-not-composed");

                    // 조건 안의 입력이 호출 경로 위쪽에서 온 것임을 표시한다.
                    if (carried.Kind != ConditionKind.Always)
                    {
                        variant.AddGap("caller-inputs-carried");
                    }
                }

                var plumbing = SingletonPlumbing.Explains(entry, outcomes);

                if (plumbing)
                {
                    variant.AddGap("singleton-plumbing");
                }

                // 효과가 없거나 조건이 불완전하면 `flow` 로 남긴다. 효과로 가는 경로를 따라가는 데 쓰인다.
                variant.RecordKind = outcomes.Count > 0 && composable && !plumbing ? "candidate" : "flow";

                variant.Outcomes.AddRange(outcomes);
                variant.Calls.AddRange(calls);
                variant.Handles.AddRange(handles);
                variant.LoopsBackTo = GoesRoundAgain(block);
                when.CollectGestures(variant.Inputs, new HashSet<Condition>());

                // 분기 조건이 아니라 메서드가 돌려주는 입력 읽기이므로 조건과 따로 표시한다.
                if (answered != null)
                {
                    variant.Inputs.Add(answered);
                    variant.AddGap("input-not-branched");
                }
                variant.Incomplete = when.HasUnknown(new HashSet<Condition>());

                if (variant.Incomplete)
                {
                    variant.AddGap("unread-condition");
                }

                variants.Add(variant);
            }
        }

        /// <summary>블록에 닿는 조건. 계산 결과는 <paramref name="reached"/> 에 캐시한다.</summary>
        internal static Condition ReachOf(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int block,
            Condition[] reached,
            byte[] state)
        {
            return Reach(graph, dependence, block, reached, state);
        }

        /// <summary>
        /// 기록이 붙을 behaviour 타입.
        /// </summary>
        /// <remarks>
        /// coroutine 과 람다는 컴포넌트가 아닌 중첩 타입으로 컴파일되므로 감싸는 behaviour 까지 올라간다.
        /// </remarks>
        private static TypeDefinition Owning(TypeDefinition type)
        {
            var current = type;

            for (var depth = 0; depth < 8 && current != null; depth++)
            {
                if (AnalysisScope.Inspect(current) == TypeVerdict.Behaviour)
                {
                    return current;
                }

                current = current.DeclaringType;
            }

            return null;
        }

        /// <summary>
        /// 블록에 닿기 위해 참이어야 하는 조건.
        /// </summary>
        /// <remarks>
        /// 블록을 다스리는 결정마다 "그 결정의 검사 AND 그 결정에 닿는 조건" 을 만들고, 그것들의 OR 를 취한다.
        ///
        /// 루프는 의존 관계를 순환하게 만든다. 각 블록은 Unvisited → Computing → Settled 로 한 번씩만 옮겨 가므로
        /// 반드시 끝난다. 계산 중인 블록을 다시 만나면 그 경로는 따라가지 않는다.
        /// </remarks>
        private static Condition Reach(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int start,
            Condition[] reached,
            byte[] state)
        {
            // 분기가 깊은 생성 메서드에서 재귀는 스택 오버플로로 에디터를 죽이므로 명시적 스택을 쓴다.
            var pending = new Stack<int>();
            pending.Push(start);

            while (pending.Count > 0)
            {
                var index = pending.Peek();

                if (state[index] == Settled)
                {
                    pending.Pop();
                    continue;
                }

                if (state[index] == Unvisited)
                {
                    state[index] = Computing;
                    var waiting = false;

                    foreach (var governor in dependence.Governing(index))
                    {
                        if (state[governor.Decision] == Unvisited)
                        {
                            pending.Push(governor.Decision);
                            waiting = true;
                        }
                    }

                    if (waiting)
                    {
                        continue;
                    }
                }

                reached[index] = Combine(graph, dependence, index, reached, state);
                state[index] = Settled;
                pending.Pop();
            }

            return reached[start];
        }

        private static Condition Combine(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int index,
            Condition[] reached,
            byte[] state)
        {
            var governors = dependence.Governing(index);

            if (governors.Count == 0)
            {
                // 이 블록을 다스리는 결정이 없다.
                return Condition.Always;
            }

            var ways = new List<Condition>(governors.Count);

            foreach (var governor in governors)
            {
                // 계산 중인 결정은 이 블록이 루프 안에 있다는 뜻이다. "한 바퀴 더 돌았다" 는 테스터가 마련할 조건이
                // 아니고, 루프 밖의 조건은 그 결정이 따로 다스리므로 Always 로 둔다. unknown 으로 두면 조건 전체가
                // 읽지 못한 것이 된다. 루프 여부는 `loopsBackTo` 로 따로 적는다.
                var earlier = state[governor.Decision] == Settled
                    ? reached[governor.Decision]
                    : Condition.Always;

                ways.Add(Condition.Every(new[]
                {
                    Literal(
                        graph.Blocks[governor.Decision],
                        graph.Blocks[governor.Taken],
                        graph,
                        dependence,
                        reached,
                        state),
                    earlier
                }));
            }

            return Condition.Either(ways);
        }

        /// <summary>합류 지점에서 읽는 들어오는 경로의 최대 수.</summary>
        private const int MaxMergeWays = 4;

        /// <summary>
        /// 단락 평가 결과를 지역 변수에 저장한 뒤 분기하는 블록의 조건.
        /// </summary>
        /// <remarks>
        /// 디버그 빌드는 <c>(A || B) &amp;&amp; C</c> 의 중간 결과를 지역 변수에 저장하고 적재해 분기하므로 블록 안에
        /// 거슬러 읽을 값이 없다.
        ///
        /// 그래서 들어오는 경로마다 올린 값을 앞으로 읽는다. 리터럴이면 진 쪽 분기이고, 비교면 그 비교가 조건이다.
        ///
        /// 경로 하나라도 읽지 못하면 null 이다. 일부 경로만 담은 조건은 틀린 선행 조건이 된다.
        /// </remarks>
        private static Condition Incoming(
            BasicBlock decision,
            BasicBlock taken,
            ControlFlowGraph graph,
            ControlDependence dependence,
            Condition[] reached,
            byte[] state)
        {
            var branch = decision.Last;
            var onTrue = branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S;
            var onFalse = branch.OpCode.Code == Code.Brfalse || branch.OpCode.Code == Code.Brfalse_S;

            if (!onTrue && !onFalse)
            {
                return null;
            }

            // 블록이 저장, 적재, 분기만 가질 때만 본다. 그때 값은 앞 블록에서 만들어졌다.
            var value = Preceding(branch, decision);

            if (!IsLoadLocal(value, out var slot))
            {
                return null;
            }

            var store = Preceding(value, decision);

            if (!IsStoreLocal(store, out var stored) || stored != slot ||
                Preceding(store, decision) != null)
            {
                return null;
            }

            if (decision.Predecessors.Count == 0 || decision.Predecessors.Count > MaxMergeWays)
            {
                return null;
            }

            var branched = ReferenceEquals(taken.First, branch.Operand as Instruction);
            var wantTrue = onTrue ? branched : !branched;
            var ways = new List<Condition>();

            foreach (var from in decision.Predecessors)
            {
                var pushed = Pushed(from);

                if (pushed == null)
                {
                    return null;
                }

                var arriving = Reach(graph, dependence, from.Index, reached, state);

                if (IlReading.TryConstant(pushed, out var literal))
                {
                    // 리터럴을 올린 경로는 그 값이 원하는 쪽일 때만 이 분기를 탄다.
                    if (literal != 0 == wantTrue)
                    {
                        ways.Add(arriving);
                    }

                    continue;
                }

                // `A || B` 의 마지막 경로는 입력 읽기 결과를 그대로 올린다. 비교로 읽으면 "GetKeyDown != 0" 이 되므로
                // 입력으로 읽는다.
                var read = ReadInput(pushed);

                if (read != null)
                {
                    read.Absent = !wantTrue;
                    ways.Add(Condition.Every(new[] { arriving, Condition.FromGesture(read) }));
                    continue;
                }

                var comparison = ComparisonOperator(pushed, wantTrue, from, out var operands);

                if (comparison == null)
                {
                    return null;
                }

                Operands(operands, from, graph.HasThis, graph.Method, out var left, out var right,
                    out var context, out _, out _, out var watch);

                if (left == null || right == null)
                {
                    return null;
                }

                ways.Add(Condition.Every(new[]
                {
                    arriving,
                    Condition.FromTest(new Precondition
                    {
                        Left = left,
                        Operator = comparison,
                        Right = right,
                        Context = context,
                        Watch = watch,
                        Offset = pushed.Offset
                    })
                }));
            }

            return ways.Count == 0 ? null : Condition.Either(ways);
        }

        /// <summary>
        /// 이 블록이 루프의 되돌아가는 edge 끝에 있을 때 제어가 되돌아오는 오프셋. 아니면 -1.
        /// </summary>
        /// <remarks>
        /// C# 컴파일러의 흐름은 reducible 하고 오프셋이 코드 순서를 따르므로 edge 양 끝의 오프셋만 비교한다. 루프
        /// 안에 있기만 한 블록은 지배 관계 분석이 필요하므로 판단하지 않는다.
        /// </remarks>
        private static int GoesRoundAgain(BasicBlock block)
        {
            var here = block.First?.Offset ?? -1;

            if (here < 0)
            {
                return -1;
            }

            // 뒤쪽 블록이 이 블록으로 되돌아온다.
            foreach (var from in block.Predecessors)
            {
                if ((from.First?.Offset ?? -1) > here)
                {
                    return here;
                }
            }

            // 이 블록이 앞쪽으로 되돌아간다.
            var earliest = -1;

            foreach (var to in block.Successors)
            {
                var there = to.First?.Offset ?? -1;

                if (there >= 0 && there <= here && (earliest < 0 || there < earliest))
                {
                    earliest = there;
                }
            }

            return earliest;
        }

        /// <summary>진단용으로 명령어의 opcode 이름을 돌려준다. 없으면 "none".</summary>
        private static string Shape(Instruction instruction)
        {
            return instruction == null ? "none" : instruction.OpCode.Name;
        }

        /// <summary>
        /// 두 경로로 들어온 블록이 저장한 값 중 실제로 계산된 쪽을 올린 명령어.
        /// </summary>
        /// <remarks>
        /// 단락된 <c>&amp;&amp;</c> 는 합류 블록 맨 위에 결과를 저장한다. 한 경로는 비교를 올리고, 다른 경로는 리터럴을
        /// 올린다. 리터럴 쪽 검사는 control dependence 로 이미 조건에 들어 있으므로 계산된 쪽이 여기의 검사다.
        ///
        /// 둘 다 계산된 값이면 삼항 연산이므로 null 이다.
        /// </remarks>
        private static Instruction JoinedAbove(BasicBlock decision)
        {
            if (decision.Predecessors.Count != 2 ||
                !IsStoreLocal(decision.First, out var slot))
            {
                return null;
            }

            // 분기가 방금 저장한 지역 변수를 검사할 때만 이 모양이다.
            var value = Preceding(decision.Last, decision);

            if (!IsLoadLocal(value, out var read) || read != slot)
            {
                return null;
            }

            Instruction worked = null;

            foreach (var before in decision.Predecessors)
            {
                var handed = Pushed(before);

                if (handed == null)
                {
                    return null;
                }

                if (IlReading.TryConstant(handed, out _))
                {
                    continue;
                }

                if (worked != null)
                {
                    // 두 경로 모두 계산된 값이면 어느 쪽인지 알 수 없다.
                    return null;
                }

                worked = handed;
            }

            return worked;
        }

        private static Instruction Pushed(BasicBlock from)
        {
            var last = from.Last;

            if (last == null)
            {
                return null;
            }

            // 무조건 점프는 앞 명령어의 값을 나른다. 조건 점프는 값을 소비하므로 올린 값이 없다.
            if (last.OpCode.FlowControl == FlowControl.Branch)
            {
                return Preceding(last, from);
            }

            return last.OpCode.FlowControl == FlowControl.Cond_Branch ? null : last;
        }

        /// <summary>결정의 한쪽 경로를 탔을 때의 조건.</summary>
        private static Condition Literal(
            BasicBlock decision,
            BasicBlock taken,
            ControlFlowGraph graph,
            ControlDependence dependence,
            Condition[] reached,
            byte[] state)
        {
            var branch = decision.Last;

            // 분기가 입력 읽기 결과를 곧바로 검사할 때만 입력으로 읽는다. 그 밖에는 방향을 알 수 없고, 방향이
            // 뒤집히면 반대로 누르라는 조건이 된다. 디버그 빌드의 지역 변수 경유도 곧바로로 본다.
            var input = ReadInput(Producer(branch, decision));

            if (input != null)
            {
                var pressedWhenBranched = branch.OpCode.Code == Code.Brtrue ||
                                          branch.OpCode.Code == Code.Brtrue_S;

                var absentWhenBranched = branch.OpCode.Code == Code.Brfalse ||
                                         branch.OpCode.Code == Code.Brfalse_S;

                if (!pressedWhenBranched && !absentWhenBranched)
                {
                    return Condition.Unreadable("input");
                }

                var branched = ReferenceEquals(taken.First, branch.Operand as Instruction);
                input.Absent = pressedWhenBranched ? !branched : branched;

                // 비교("GetKeyDown != 0")가 아니라 입력으로 적는다.
                return Condition.FromGesture(input);
            }

            if (InputIn(decision) != null)
            {
                return Condition.Unreadable("input");
            }

            if (IsResumeDispatch(decision, graph.StateSlot))
            {
                return Condition.Always;
            }

            if (branch.OpCode.Code == Code.Switch)
            {
                return SwitchCase(decision, taken, graph);
            }

            var incoming = Incoming(decision, taken, graph, dependence, reached, state);

            if (incoming != null)
            {
                return incoming;
            }

            var precondition = ReadCondition(decision, taken, graph.HasThis, graph.Method, out var unread);

            return precondition == null
                ? Condition.Unreadable("condition", unread)
                : Condition.FromTest(precondition);
        }

        /// <summary>
        /// 이 결정이 coroutine 의 재개 지점 분배인지 본다.
        /// </summary>
        /// <remarks>
        /// coroutine 의 <c>MoveNext</c> 는 먼저 어느 <c>yield</c> 에서 멈췄는지로 분기한다. 이것은 컴파일러의 상태
        /// 필드 검사라 게임 조건이 아니므로 Always 로 본다. 원래 코드의 조건은 뒤따르는 블록에 그대로 있다.
        /// </remarks>
        private static bool IsResumeDispatch(BasicBlock block, int stateSlot)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                if (instruction.OpCode.Code == Code.Ldfld &&
                    instruction.Operand is FieldReference field &&
                    field.Name == StateField &&
                    field.DeclaringType != null &&
                    field.DeclaringType.Name.StartsWith("<", System.StringComparison.Ordinal))
                {
                    return true;
                }

                // 같은 분배의 뒤쪽 블록은 첫 블록이 만든 지역 변수 복사본을 검사한다.
                if (stateSlot >= 0 && IsLoadLocal(instruction, out var slot) && slot == stateSlot)
                {
                    return true;
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>coroutine 이 멈춘 yield 위치를 담는 컴파일러 생성 필드 이름.</summary>
        private const string StateField = "<>1__state";

        private static InputRead InputIn(BasicBlock block)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var read = ReadInput(instruction);

                if (read != null)
                {
                    return read;
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return null;
        }

        private static InputRead ReadInput(Instruction instruction)
        {
            if (instruction == null ||
                !(instruction.Operand is MethodReference called) ||
                instruction.OpCode.FlowControl != FlowControl.Call ||
                !ReadsInput(called.DeclaringType?.FullName))
            {
                return null;
            }

            // `UnityEngine.Input` 과 `VirtualInput` 은 멤버 이름이 같다.
            switch (called.Name)
            {
                case "GetKeyDown": return Key(instruction, called, "down");
                case "GetKey": return Key(instruction, called, "held");
                case "GetKeyUp": return Key(instruction, called, "up");
                case "GetMouseButtonDown": return Mouse(instruction, "down");
                case "GetMouseButton": return Mouse(instruction, "held");
                case "GetMouseButtonUp": return Mouse(instruction, "up");
                case "get_anyKeyDown": return new InputRead { Gesture = "key", Name = "any", Phase = "down", Offset = instruction.Offset };
                case "get_anyKey": return new InputRead { Gesture = "key", Name = "any", Phase = "held", Offset = instruction.Offset };
                default: return null;
            }
        }

        /// <summary>선언 타입이 원래 입력 클래스나 weaver 가 바꾼 타입인지 본다.</summary>
        private static bool ReadsInput(string declaringType) =>
            declaringType == InputType || declaringType == ProxiedInputType;

        private static InputRead Key(Instruction instruction, MethodReference called, string phase)
        {
            var read = new InputRead { Gesture = "key", Phase = phase, Offset = instruction.Offset };
            var argument = instruction.Previous;

            if (argument != null && argument.OpCode.Code == Code.Ldstr)
            {
                read.Name = argument.Operand as string;
                return read;
            }

            if (IlReading.TryConstant(argument, out var value) && called.Parameters.Count == 1)
            {
                read.Name = IlReading.EnumName(called.Parameters[0].ParameterType, value);
                return read;
            }

            // 변수에 담긴 키는 알 수 없지만 입력이 있다는 사실은 남긴다.
            read.Name = "(not a literal)";
            return read;
        }

        private static InputRead Mouse(Instruction instruction, string phase)
        {
            var read = new InputRead { Gesture = "mouse", Phase = phase, Offset = instruction.Offset };
            read.Name = IlReading.TryConstant(instruction.Previous, out var button)
                ? button.ToString()
                : "(not a literal)";
            return read;
        }

        /// <summary>
        /// 이 블록의 직접 효과. 피호출자의 효과는 피호출자의 기록에 남는다.
        /// </summary>
        /// <remarks>
        /// 피호출자의 효과를 복사하면 그 조건을 잃어 서로 배타적인 씬 로드가 함께 일어나는 것처럼 보인다.
        /// </remarks>
        private static List<Outcome> OutcomesIn(BasicBlock block, MethodDefinition method)
        {
            var outcomes = new List<Outcome>();

            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var outcome = OutcomeReader.ReadDirect(instruction, block.First, method);

                if (outcome != null)
                {
                    outcomes.Add(outcome);
                }
                if (instruction == block.Last)
                {
                    break;
                }
            }

            return outcomes;
        }

        private static List<CallEdge> CallsIn(BasicBlock block, ModuleDefinition module, bool hasThis)
        {
            var calls = new List<CallEdge>();

            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var callee = CallGraph.CalleeAt(instruction, module);

                if (callee != null)
                {
                    var reference = instruction.Operand as MethodReference;

                    calls.Add(new CallEdge
                    {
                        TargetId = MethodIdentity.Of(callee),
                        Target = callee.FullName,
                        Receiver = IlReading.Receiver(reference, instruction, block.First),
                        ReceiverWhere = IlReading.ReceiverWhere(
                            reference, instruction, block.First, hasThis),
                        Arguments = IlReading.Arguments(reference, instruction, block.First),
                        Offset = instruction.Offset
                    });
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return calls;
        }

        /// <summary>
        /// 결정의 비교를 <paramref name="taken"/> 경로에서 참이 되는 형태로 돌려준다.
        /// </summary>
        /// <remarks>
        /// 분기 연산자는 점프하는 조건이므로 fall-through 경로에서는 부정한다.
        /// </remarks>
        private static Precondition ReadCondition(
            BasicBlock decision, BasicBlock taken, bool hasThis, MethodDefinition method,
            out string unread)
        {
            var branch = decision.Last;
            unread = null;

            if (!(branch.Operand is Instruction target))
            {
                // switch 는 피연산자 둘을 비교하지 않으므로 여기서 읽지 않는다.
                unread = "branch:" + branch.OpCode.Name;
                return null;
            }

            var branched = ReferenceEquals(taken.First, target);
            var comparison = Operator(branch.OpCode.Code, branched);

            if (comparison == null)
            {
                unread = "operator:" + branch.OpCode.Name;
                return null;
            }

            string left;
            string right;
            string context;
            Instruction at;
            string lost = null;
            WatchTarget watch = null;

            if (branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S ||
                branch.OpCode.Code == Code.Brfalse || branch.OpCode.Code == Code.Brfalse_S)
            {
                var producer = Producer(branch, decision) ?? JoinedAbove(decision);

                // 디버그 빌드는 비교 결과를 brtrue/brfalse 로 검사한다. "결과 != 0" 이 아니라 원래 비교를 조건으로 쓴다.
                var holds = branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S
                    ? branched
                    : !branched;

                var compared = ComparisonOperator(producer, holds, decision, out var operands);

                if (compared != null)
                {
                    Operands(operands, decision, hasThis, method, out left, out right, out context,
                        out at, out lost, out watch);
                    comparison = compared;
                }
                else
                {
                    left = IlReading.Describe(producer, decision.First, method);
                    right = "0";
                    context = IlReading.Where(
                        producer, decision.First, hasThis, method, out var singleStop);
                    lost = context == null ? "single:" + Shape(singleStop) : null;
                    watch = WatchTarget.From(IlReading.Holding(producer, method))
                            ?? WatchTarget.ReadOff(producer, decision.First, method);
                    at = producer;
                }
            }
            else
            {
                Operands(branch, decision, hasThis, method, out left, out right, out context,
                    out at, out lost, out watch);
            }

            if (left == null || right == null)
            {
                unread = "operand:" + (at == null ? "none" : at.OpCode.Name);
                return null;
            }

            return new Precondition
            {
                Left = left,
                Operator = comparison,
                Right = right,
                Context = context,
                SubjectLost = lost,
                Watch = watch,
                Offset = branch.Offset
            };
        }

        /// <summary>
        /// <paramref name="taken"/> 경로가 switch 의 어느 case 인지를 조건으로 만든다.
        /// </summary>
        /// <remarks>
        /// 여러 case 가 한 블록을 공유할 수 있어(<c>case 4:</c> <c>case 5:</c>) 결과는 case 들의 OR 다. case 가 0 에서
        /// 시작하지 않으면 앞에 뺄셈이 붙어 컴파일되므로 그만큼 보정한다.
        ///
        /// fall-through(default) 경로는 비교 한 쌍으로 쓴다. IL switch 는 부호 없이 비교하므로 음수도 default 로 간다.
        /// </remarks>
        private static Condition SwitchCase(BasicBlock decision, BasicBlock taken, ControlFlowGraph graph)
        {
            if (!(decision.Last.Operand is Instruction[] targets) || targets.Length == 0)
            {
                return Condition.Unreadable("switch");
            }

            var subject = Producer(decision.Last, decision);
            var offset = 0;

            // case 가 0 에서 시작하지 않으면 앞에 add/sub 가 있다.
            if (subject != null && (subject.OpCode.Code == Code.Sub || subject.OpCode.Code == Code.Add))
            {
                var shift = Preceding(subject, decision);

                if (!IlReading.TryConstant(shift, out var amount))
                {
                    return Condition.Unreadable("switch");
                }

                offset = subject.OpCode.Code == Code.Sub ? amount : -amount;
                subject = Preceding(shift, decision);
            }

            var name = IlReading.Describe(subject, decision.First, graph.Method)
                       ?? Argument(subject, graph.Method);

            if (name == null)
            {
                return Condition.Unreadable("switch");
            }

            var where = IlReading.Where(
                subject, decision.First, graph.HasThis, graph.Method, out var stoppedAt);

            // switch 는 <see cref="Operands"/> 를 거치지 않으므로 SubjectLost 를 여기서 채운다.
            var lost = where == null ? "switch:" + Shape(stoppedAt) : null;
            var cases = new List<Condition>();

            for (var index = 0; index < targets.Length; index++)
            {
                if (ReferenceEquals(taken.First, targets[index]))
                {
                    cases.Add(Condition.FromTest(new Precondition
                    {
                        Left = name,
                        Operator = "==",
                        Right = (index + offset).ToString(),
                        Context = where,
                        SubjectLost = lost,
                        Offset = decision.Last.Offset
                    }));
                }
            }

            if (cases.Count > 0)
            {
                return Condition.Either(cases);
            }

            // 어느 case 도 아니면 default 경로다.
            return Condition.Every(new[]
            {
                Condition.FromTest(new Precondition
                {
                    Left = name, Operator = ">=", Right = offset.ToString(),
                    Context = where, SubjectLost = lost, Offset = decision.Last.Offset
                }),
                Condition.FromTest(new Precondition
                {
                    Left = name, Operator = ">=", Right = (targets.Length + offset).ToString(),
                    Context = where, SubjectLost = lost, Offset = decision.Last.Offset
                })
            });
        }

        /// <summary>
        /// 인자 적재 명령어가 가리키는 매개변수 이름.
        /// </summary>
        /// <remarks>
        /// switch 의 대상이 인자일 때 이름이 없으면 조건을 쓸 수 없어서 쓴다. <c>context</c> 가 <c>arg:N</c> 이므로
        /// receiver 상태로 오해되지 않는다.
        /// </remarks>
        private static string Argument(Instruction instruction, MethodDefinition method)
        {
            if (instruction == null || method == null)
            {
                return null;
            }

            int index;

            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0: index = 0; break;
                case Code.Ldarg_1: index = 1; break;
                case Code.Ldarg_2: index = 2; break;
                case Code.Ldarg_3: index = 3; break;
                case Code.Ldarg:
                case Code.Ldarg_S:
                    return (instruction.Operand as ParameterDefinition)?.Name;
                default: return null;
            }

            if (method.HasThis)
            {
                index--;
            }

            return index >= 0 && index < method.Parameters.Count ? method.Parameters[index].Name : null;
        }

        /// <summary>
        /// 비교의 두 피연산자와 기준 객체, 감시 대상.
        /// </summary>
        /// <remarks>
        /// 오른쪽은 바로 앞 명령어이고, 왼쪽은 오른쪽이 소비한 슬롯을 <see cref="IlReading.Under"/> 로 건너뛰어 찾는다.
        /// 명령어 하나만 되짚으면 <c>a == b.Count</c> 가 <c>b == b.Count</c> 로 읽힌다. 건너뛸 수 없으면 왼쪽은 null 이다.
        /// </remarks>
        private static void Operands(
            Instruction consumer,
            BasicBlock decision,
            bool hasThis,
            MethodDefinition method,
            out string left,
            out string right,
            out string context,
            out Instruction unreadAt,
            out string lost,
            out WatchTarget watch)
        {
            var boundary = decision.First;
            var rightAt = IlReading.Preceding(consumer, boundary);
            var leftAt = IlReading.Under(rightAt, boundary);

            right = IlReading.Describe(rightAt, boundary, method);
            left = IlReading.Describe(leftAt, boundary, method);

            // 이름과 감시 대상이 어긋나지 않도록 같은 `leftAt` 에서 찾는다. 필드를 먼저 보고, 없으면
            // `spellCards.Count` 처럼 getter 의 receiver 필드를 본다.
            watch = WatchTarget.From(IlReading.Holding(leftAt, method))
                    ?? WatchTarget.ReadOff(leftAt, boundary, method);

            // 진단용으로 읽지 못한 쪽을 적는다. 보통 실패하는 쪽인 왼쪽을 먼저 본다.
            unreadAt = left == null ? leftAt : (right == null ? rightAt : null);

            // 양쪽 기준 객체가 일치해야 한다. 다르면 조건을 호출자 기준으로 바꿔 쓸 수 없다. 상수 쪽은 무엇과도 일치한다.
            var leftWhere = IlReading.Where(leftAt, boundary, hasThis, method, out var leftStop);
            var rightWhere = IlReading.Where(rightAt, boundary, hasThis, method, out var rightStop);

            context = IlReading.Agreeing(leftWhere, rightWhere);
            lost = context != null
                ? null
                : leftWhere == null && rightWhere == null
                    ? "both:" + Shape(leftStop) + "/" + Shape(rightStop)
                    : leftWhere == null
                        ? "left:" + Shape(leftStop)
                        : rightWhere == null
                            ? "right:" + Shape(rightStop)
                            : "disagree:" + leftWhere + "/" + rightWhere;
        }

        /// <summary>
        /// <paramref name="consumer"/> 가 소비하는 값을 실제로 만든 명령어.
        /// </summary>
        /// <remarks>
        /// 릴리스 빌드는 값을 스택에 남기지만 디버그 빌드는 지역 변수에 저장하고 바로 적재한다. 두 빌드 모두 읽어야 한다.
        ///
        /// 저장이 적재 바로 앞에 있을 때만 따라간다. 떨어진 저장은 다른 대입이 끼었을 수 있다.
        /// </remarks>
        private static Instruction Producer(Instruction consumer, BasicBlock within)
        {
            var value = Preceding(consumer, within);

            if (!IsLoadLocal(value, out var slot))
            {
                return value;
            }

            var store = Preceding(value, within);

            return IsStoreLocal(store, out var stored) && stored == slot
                ? Preceding(store, within)
                : value;
        }

        /// <summary>블록 시작을 넘지 않는 앞 명령어.</summary>
        private static Instruction Preceding(Instruction instruction, BasicBlock within)
        {
            return IlReading.Preceding(instruction, within?.First);
        }

        private static bool IsLoadLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0: slot = 0; return true;
                case Code.Ldloc_1: slot = 1; return true;
                case Code.Ldloc_2: slot = 2; return true;
                case Code.Ldloc_3: slot = 3; return true;
                case Code.Ldloc:
                case Code.Ldloc_S:
                    slot = (instruction.Operand as VariableReference)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        private static bool IsStoreLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Stloc_0: slot = 0; return true;
                case Code.Stloc_1: slot = 1; return true;
                case Code.Stloc_2: slot = 2; return true;
                case Code.Stloc_3: slot = 3; return true;
                case Code.Stloc:
                case Code.Stloc_S:
                    slot = (instruction.Operand as VariableReference)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        /// <summary>
        /// 값으로 남은 비교(<c>ceq</c>, <c>clt</c>, <c>cgt</c>, 연산자 메서드)의 연산자.
        /// </summary>
        /// <remarks>
        /// 디버그 빌드는 <c>if (a == b)</c> 를 <c>beq</c> 대신 <c>ceq</c> 와 분기로 만든다. <c>&gt;=</c> 와
        /// <c>&lt;=</c> 는 <c>clt; ldc.i4.0; ceq</c> 처럼 부정으로 오므로 여기서 한 연산자로 접는다.
        /// </remarks>
        private static string ComparisonOperator(
            Instruction instruction, bool holds, BasicBlock within, out Instruction operands)
        {
            operands = instruction;

            if (instruction == null)
            {
                return null;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ceq:
                    // 0 과의 ceq 는 부정이다. 부정 대상이 비교면 연산자 하나로 접는다.
                    var zero = Preceding(instruction, within);

                    if (IlReading.TryConstant(zero, out var value) && value == 0)
                    {
                        var negated = Preceding(zero, within);
                        var inner = ComparisonOperator(negated, !holds, within, out operands);

                        if (inner != null)
                        {
                            return inner;
                        }

                        operands = instruction;
                    }

                    return holds ? "==" : "!=";

                case Code.Clt:
                case Code.Clt_Un:
                    return holds ? "<" : ">=";

                case Code.Cgt:
                case Code.Cgt_Un:
                    return holds ? ">" : "<=";

                case Code.Call:
                case Code.Callvirt:
                    return OperatorMethod(instruction.Operand as MethodReference, holds);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 연산자 메서드로 컴파일된 비교의 연산자.
        /// </summary>
        /// <remarks>
        /// 문자열, Unity 객체의 null 비교, <c>==</c> 를 정의한 구조체는 <c>op_Equality</c> 등의 호출이 된다. 이것이
        /// 없으면 <c>String.op_Equality() != 0</c> 으로 읽힌다.
        ///
        /// 비교 연산자 여섯만 받는다. 산술 연산자는 비교의 피연산자 쪽에 속한다.
        /// </remarks>
        private static string OperatorMethod(MethodReference method, bool holds)
        {
            if (method == null || method.Parameters.Count != 2 || method.HasThis)
            {
                return null;
            }

            switch (method.Name)
            {
                case "op_Equality": return holds ? "==" : "!=";
                case "op_Inequality": return holds ? "!=" : "==";
                case "op_LessThan": return holds ? "<" : ">=";
                case "op_GreaterThan": return holds ? ">" : "<=";
                case "op_LessThanOrEqual": return holds ? "<=" : ">";
                case "op_GreaterThanOrEqual": return holds ? ">=" : "<";
                default: return null;
            }
        }

        private static string Operator(Code code, bool branched)
        {
            switch (code)
            {
                case Code.Beq:
                case Code.Beq_S:
                    return branched ? "==" : "!=";

                case Code.Bne_Un:
                case Code.Bne_Un_S:
                case Code.Brtrue:
                case Code.Brtrue_S:
                    return branched ? "!=" : "==";

                case Code.Brfalse:
                case Code.Brfalse_S:
                    return branched ? "==" : "!=";

                case Code.Bgt:
                case Code.Bgt_S:
                case Code.Bgt_Un:
                case Code.Bgt_Un_S:
                    return branched ? ">" : "<=";

                case Code.Bge:
                case Code.Bge_S:
                case Code.Bge_Un:
                case Code.Bge_Un_S:
                    return branched ? ">=" : "<";

                case Code.Blt:
                case Code.Blt_S:
                case Code.Blt_Un:
                case Code.Blt_Un_S:
                    return branched ? "<" : ">=";

                case Code.Ble:
                case Code.Ble_S:
                case Code.Ble_Un:
                case Code.Ble_Un_S:
                    return branched ? "<=" : ">";

                default:
                    return null;
            }
        }

    }
}
