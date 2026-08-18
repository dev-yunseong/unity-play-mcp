using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 그래프로 만든 메서드를 입력·선행조건·결과로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 결과에서 거슬러 읽는다. 코드가 만드는 모든 변화는 어떤 블록 안에 앉아 있고, 그 블록이 control dependent 한
    /// 결정들은 — 그것들이 다시 의존하는 결정들까지 따라 올라가면 — 플레이어가 거기 닿는 경위의 완전한 진술이다.
    /// 그중 입력을 검사하는 것들이 어떤 입력인지를 말하고, 나머지가 그 밖에 무엇이 참이어야 했는지를 말한다.
    ///
    /// 이렇게 읽으면 <c>A || B</c> 에 특별한 경우가 필요 없다. 두 검사가 같은 결과를 다스리므로 둘 다 나타나고,
    /// 그것이 코드가 뜻하는 바다.
    /// </remarks>
    internal static class VariantBuilder
    {
        private const string InputType = "UnityEngine.Input";

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
            // 메서드 전체에 걸쳐 쥐고 있는다. 각 블록의 조건은 많아야 한 번 알아내고, 메서드 위쪽의 블록은 나머지
            // 대부분으로 가는 길목에 있다.
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

                // 블록 안에 다른 것 없이 입력만 읽는 경우. `WaitUntil` 에 건넨 술어가 정확히 그 모양이다 —
                // `() => Input.GetKeyDown(Space)` 는 그것으로 분기하는 대신 답을 돌려주므로 그것으로 제스처가 만들어지는
                // 일이 없고, 그 블록이 키를 언급했다는 것을 아무도 알아채기 전에 떨어져 나간다. 샘플 게임의 이야기 화면
                // 전체가 Space 로 넘어갔는데 리포트는 키가 하나도 없다고 말했다.
                //
                // 분기하는 블록은 건드리지 않는다. 그 읽기는 그것이 다스리는 모든 것의 조건 안에 이미 제스처로 들어 있고,
                // 여기서 한 번 더 말하면 그것을 두 배로 만들면서 분기하지 않은 것으로 표시하게 되는데, 그것이야말로
                // 그 블록이 아닌 유일한 것이다.
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

                // 서로 다른 두 수신자에 대고 쓰인 두 조건을 한 문장으로 이어 붙여서는 안 된다: 피호출자의 `count > 0` 은
                // 피호출자의 객체에 대한 것이고, 호출자 자신의 용어 옆에서는 호출자의 `count` 로 읽힌다. 그 경우만 건드리지
                // 않는다.
                //
                // 섞이는 것이 위험의 전부이므로, 섞이지 않는 것은 무엇이든 안전하다. 지켜지지 않은 호출은 합성할 것을
                // 내놓지 않으므로 피호출자 자신의 조건이 거기 닿는 경위의 완전한 진술로 남는다. 제 조건이 없는 피호출자는
                // 호출자의 것을 남긴다. 어느 쪽이든 정확히 한쪽만 말하고, 제 용어로 말한다.
                // 두 조건이 같은 객체에 대한 것임이 알려져 있을 때는 이어 붙여도 된다. 가는 길의 모든 호출이 호출자 자신의
                // 객체에 대고 이루어졌고 — 그러면 `this` 는 양 끝에서 같은 것이다 — 양쪽이 `this` 에 대한 것이나 아무것에
                // 대한 것도 아닌 말만 할 때가 그 경우다.
                //
                // 일부러 이만큼 좁게 둔다. 대안인 추측이 완벽하게 읽히면서 엉뚱한 객체에 대한 문장을 만들어낸다는 것이
                // 실측됐다.
                // 호출자가 이름 붙일 수 있는 것에 대고 그것을 불렀을 때, 호출자가 선 자리에서 말한 것. 그러면 피호출자의
                // 용어가 호출자 자신의 객체를 서술하고 문장에 주어가 하나가 되는데, 그것이 아래 규칙이 청하는 전부다.
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

                // 섞이게 될 자리에서는 호출자의 용어를 떨어뜨리고 그중 입력만 내려온다. 입력은 조건에서 객체에 대한 것이
                // 아닌 유일한 부분이다: 호출자의 `count > 0` 은 피호출자의 용어 옆에서 다른 뜻이 되지만, 호출자의
                // `Space 가 눌렸다` 는 어디에 쓰이든 같은 뜻이다.
                //
                // 이것이 없으면 키를 한 메서드에서 읽고 일을 다른 메서드에서 하는 게임은 근거 어디에도 입력이 없다.
                // Trash Dash 가 그런 게임이다 — 그것이 읽는 모든 키가 그것이 가진 모든 효과에서 호출 하나만큼 떨어져
                // 있고, 리포트는 그중 하나의 이름도 대지 않았다. 조건은 여전히 불완전하고 여전히 그렇다고 말한다.
                // 달라지는 것은, 이제 플레이어가 무엇을 했는지에 대해 침묵하는 것이 아니라 *그 밖에* 무엇이 참이어야
                // 했는지에 대해 불완전하다는 점이다.
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
                    // 문장에 이제 저자가 둘이므로 말해 둔다. 그것은 한 객체에 대한 하나의 진술이고, 어느 부분이 어디서 왔는지는
                    // 그 안에서 더는 보이지 않는다.
                    variant.AddGap("composed-on-same-object");
                }

                if (derived && !composable)
                {
                    variant.AddGap("callee-condition-not-composed");

                    // 조건을 어떻게 읽어야 하는지가 달라지므로 따로 말한다: 그 안의 입력은 여기가 아니라 호출 경로 위쪽
                    // 어딘가에서 주어졌다.
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

                // 행동할 것이 없거나, 여기 오는 경위의 완전한 진술이 없다. 제 효과가 없는 호출이 곧 효과로 가는 경로를
                // 따라가는 방법이므로 남겨 둔다.
                variant.RecordKind = outcomes.Count > 0 && composable && !plumbing ? "candidate" : "flow";

                variant.Outcomes.AddRange(outcomes);
                variant.Calls.AddRange(calls);
                variant.Handles.AddRange(handles);
                variant.LoopsBackTo = GoesRoundAgain(block);
                when.CollectGestures(variant.Inputs, new HashSet<Condition>());

                // 조건이 아니다: 여기서는 아무것도 그것으로 분기하지 않는다. 메서드의 답이 곧 그 읽기이고, 그것을 건넨
                // 경로 옆에 그렇다고 말하는 것이 알려진 것의 전부다.
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

        /// <summary>블록이 도는 조건. 한 번 알아내고 쥐고 있는다.</summary>
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
        /// 결과가 구워지는 타입.
        /// </summary>
        /// <remarks>
        /// 컴파일러는 코루틴 본문이나 람다를 제 중첩 타입 안에 넣는데, 그 타입은 컴포넌트가 아니다. 스캔이
        /// GameObject 위에서 찾는 것은 그것들이 그 안에 쓰인 behaviour 이므로, 결과가 도착해야 하는 자리가 거기다.
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
        /// 블록에 닿기 위해 참이어야 했던 것.
        /// </summary>
        /// <remarks>
        /// 블록으로 들어가는 각 갈래는 대안이고, 한 걸음 더 거슬러 가면 그것은 추가 요구다 — 그래서 답은 갈래들
        /// 사이의 선택이고, 각 갈래는 그 갈래 위의 검사와 그 검사에 닿기까지 필요했던 것을 합한 것이다.
        ///
        /// 루프는 이 그래프를 순환하게 만든다: 루프 꼭대기의 검사는 자기 자신에 매여 있다. 이미 계산 중인 블록은
        /// 그렇게 표시되고, 그 표시를 다시 만나면 원을 따라가는 대신 그 자리에 unknown 을 놓는다. 모든 블록이
        /// 미방문에서 계산중으로, 계산중에서 확정으로 정확히 한 번씩 옮겨 가는데, 이것을 끝나게 하는 것이 그것이지
        /// 얼마나 걸릴지를 가두는 것이 아니다.
        /// </remarks>
        private static Condition Reach(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int start,
            Condition[] reached,
            byte[] state)
        {
            // 명시적 스택. 조건은 메서드가 분기하는 만큼 깊이 중첩되고, 생성된 메서드를 재귀로 걷는 일이 빌드를 죽은
            // 에디터로 바꾸는 방법이다.
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
                // 이것이 도는지를 결정하는 것이 없다.
                return Condition.Always;
            }

            var ways = new List<Condition>(governors.Count);

            foreach (var governor in governors)
            {
                // 아직 계산 중인 지배자는 이 블록이 그 루프 안에 있다는 뜻이고, "다시 한 바퀴 돌아서 여기 왔다" 는 테스터가
                // 마련하는 종류의 것이 아니다. 제어를 한 바퀴 돌려보내는 검사는 이미 이것 옆의 `Literal` 이고, 루프를 바깥에서
                // 지키던 것이 무엇이든 그것은 제 몫으로 이 블록을 다스린다 — `if` 를 탔을 때만 닿는 블록은 루프가 사이에
                // 있든 없든 그 `if` 에 control dependent 하다. 그래서 도는 갈래는 아무것도 더하지 않고, 그것에 대해
                // `unknown` 이라고 말한 것이 조건 전체를 함께 끌어내렸다: 샘플 게임의 기록 스물여섯이
                // `i < cards.Count and <아무도 읽을 수 없는 무언가>` 로 읽혔다.
                //
                // 그 블록이 루프 안에 있다는 사실은 잃지 않는다. 그것은 블록에 대한 사실이고, 조건을 쓰지 못한 실패가 아니라
                // 그래프에서 알아낸 것이며, 기록이 `loopsBackTo` 로 그것을 나른다.
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

        /// <summary>합류 지점으로 들어오는 갈래를 포기하기 전까지 몇 개나 읽는지.</summary>
        private const int MaxMergeWays = 4;

        /// <summary>
        /// 단락 평가가 분기더러 검사하라고 남긴 값.
        /// </summary>
        /// <remarks>
        /// 디버그 빌드는 `(A || B) &amp;&amp; C` 를 스택에 남기지 않는다. 각 검사를 한 블록에서 답을 계산해 저장하고,
        /// 그것을 적재한 것으로 분기한다 — 그래서 분기를 쥔 블록은 저장으로 시작하고 그 뒤에 읽을 것이 없다. 샘플
        /// 게임의 화살표 키 여섯이 개발 빌드에서 이렇게 사라졌고 에디터에서는 하나도 사라지지 않았다. 최적화하는
        /// 컴파일러는 걷기가 여전히 볼 수 있는 자리에 값을 남기기 때문이다. 두 빌드가 같은 말을 하는 것처럼 읽히고
        /// 있었다.
        ///
        /// 블록을 지나 거슬러 읽는 대신 들어오는 갈래들에서 앞으로 읽는다. 각 갈래는 리터럴을 밀어 넣었거나 — 잃은
        /// 쪽 분기 — 제가 한 비교를 밀어 넣었고, 그 갈래에 닿는 일은 이미 알아낸 조건이다. 여기서는 값을 추측하려고
        /// 블록 경계를 넘는 것이 없다: 각 블록에게 그 블록 자신의 용어로 무엇을 남겼는지 물을 뿐이다.
        ///
        /// 일부가 아니라 통째로 포기한다. 읽을 수 없는 갈래 하나는 나머지를 이것이 언제 도는지에 대한 반쪽 진술로
        /// 만들고, 조건의 반쪽 진술은 그냥 틀린 선행 조건의 모양이다.
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

            // 블록에 정말로 제 것이 하나도 없을 때만: 분기가 읽는 저장이 그 안의 첫 번째 것이므로 값은 다른 데서
            // 만들어졌다.
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
                    // 이미 진 쪽 갈래다. 제어가 여기 온 경로일 수 없다.
                    if (literal != 0 == wantTrue)
                    {
                        ways.Add(arriving);
                    }

                    continue;
                }

                // 들어온 갈래가 무언가를 비교한 것이 아니라 검사 자체를 했다: 뒤에 아무것도 없는 `A || B` 는 둘째 읽기를
                // 스택에 남긴다. 그것을 비교로 읽으면 "GetKeyDown != 0" 이 되는데, 그것은 테스터에게 하라고 시킬 수 있는
                // 것이 아니다.
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
        /// 이 블록이 다시 한 바퀴 도는 일의 일부일 때 제어가 되돌아오는 자리, 또는 -1.
        /// </summary>
        /// <remarks>
        /// 메서드의 뒤쪽에서 도착하거나 앞쪽으로 떠나는 엣지가 검사의 전부다. C# 컴파일러가 뱉는 것은 축약 가능한
        /// 흐름이고 오프셋은 코드를 따르므로, 이것은 엣지 양 끝의 두 블록만 묻고 그 밖에는 아무것도 묻지 않는다 —
        /// 지배자도, 알아낸 루프 본문도 없다. 그저 루프 안에 있기만 한 블록은 주장하지 않는다. 주장하려면 이것이
        /// 일부러 묻지 않는 물음이 필요하기 때문이다.
        /// </remarks>
        private static int GoesRoundAgain(BasicBlock block)
        {
            var here = block.First?.Offset ?? -1;

            if (here < 0)
            {
                return -1;
            }

            // 제어가 여기로 돌아온다: 뒤의 무언가가 이 블록으로 되뛴다.
            foreach (var from in block.Predecessors)
            {
                if ((from.First?.Offset ?? -1) > here)
                {
                    return here;
                }
            }

            // 되뛰는 쪽이 이 블록이다.
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

        /// <summary>블록이 다음에 도는 무엇을 위해 스택에 남긴 것.</summary>
        private static string Shape(Instruction instruction)
        {
            return instruction == null ? "none" : instruction.OpCode.Name;
        }

        /// <summary>
        /// 블록이 건네받은 값. 들어오는 갈래 중 하나만 실제로 그것을 계산했을 때.
        /// </summary>
        /// <remarks>
        /// 단락된 <c>&amp;&amp;</c> 는 두 갈래로 닿는 블록의 맨 위에 제 답을 저장한다: 한 갈래는 방금 무언가를
        /// 비교했고, 다른 갈래는 왼쪽이 이미 결판냈기 때문에 리터럴을 들고 곧장 여기로 뛰었다. 저장에서 거슬러 읽으면
        /// 경계에 닿아 포기하게 되는데, 그 때문에 개발 빌드의 조건 쉰다섯이 읽히지 않았다 — 하나같이 <c>stloc</c> 으로
        /// 시작하는, 들어오는 갈래가 둘인 블록이었다.
        ///
        /// 리터럴은 두 번째 답이 아니다. 그것은 컴파일러가 만든 점프의 모양이고, 그것이 대표하는 검사는 이미 이 블록의
        /// 조건 안에 제 몫으로 들어 있다: 그 블록은 단락을 일으킨 그 결정에 control dependent 하다. 그래서 한 갈래가
        /// 상수를 나르고 다른 갈래가 읽을 수 있는 것을 나르면, 읽을 수 있는 쪽이 여기서 검사된 것이다.
        ///
        /// 둘 다 읽히면 그것은 단락이 아니라 두 값 중 하나를 고르는 것 — 삼항 — 이고 어느 쪽도 답이 아니다. 둘 다
        /// 상수이면 어느 쪽에도 읽을 것이 없었던 것이다.
        /// </remarks>
        private static Instruction JoinedAbove(BasicBlock decision)
        {
            if (decision.Predecessors.Count != 2 ||
                !IsStoreLocal(decision.First, out var slot))
            {
                return null;
            }

            // 분기가 저장된 바로 그것을 검사하고 있어야 한다. 아니면 이것은 우연히 저장으로 시작하는 다른 블록이다.
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
                    // 둘 다 무언가를 계산한 두 갈래. 이 읽기가 그중 어느 쪽을 보았는지는 여기서 묻지 않는 물음이다.
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

            // 무조건 점프는 그 앞에서 만든 값을 나르고, 조건 점프는 만든 것이 아니라 결정한 것이라 가져갈 제 것이 없다.
            if (last.OpCode.FlowControl == FlowControl.Branch)
            {
                return Preceding(last, from);
            }

            return last.OpCode.FlowControl == FlowControl.Cond_Branch ? null : last;
        }

        /// <summary>한 결정을 한쪽으로 탔을 때 그것이 하는 말.</summary>
        private static Condition Literal(
            BasicBlock decision,
            BasicBlock taken,
            ControlFlowGraph graph,
            ControlDependence dependence,
            Condition[] reached,
            byte[] state)
        {
            var branch = decision.Last;

            // 분기가 그 입력의 답을 곧바로 검사할 때만. 블록의 다른 어디에서든 어느 쪽이 눌린 것을 뜻하는지 알 수 없고,
            // 거꾸로 기록된 제스처는 선행 조건을 반대 키를 누르라는 지시로 바꾼다.
            //
            // "곧바로" 는 디버그 빌드가 답을 저장하는 지역 변수를 거쳐 읽는데, 그것은 최적화하지 않는 컴파일러가 쓰는
            // 방식으로 쓰인 같은 검사다.
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

                // 검사 자체가 제스처다. 그것을 비교로 읽으면 "GetKeyDown != 0" 이 나오는데, 그것은 명세가 쓸 수 있는 말을
                // 하나도 하지 않는다.
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
        /// 이 결정이 코루틴이 멈춘 자리에서 다시 시작하는 것인지.
        /// </summary>
        /// <remarks>
        /// 코루틴은 상태 기계로 컴파일되고, <c>MoveNext</c> 가 맨 먼저 하는 일은 어느 <c>yield</c> 에서 멈췄는지로
        /// 분기하는 것이다. control dependence 는 그것을 평범한 결정으로 보고 그 뒤의 모든 것이 무언가에 지켜지는
        /// 것으로 읽힌다 — 그리고 그것이 검사하는 필드는 게임의 것이 아니라 컴파일러의 것이므로, 그 무언가는 아무도
        /// 읽을 수 없는 조건으로 나온다. 샘플 게임에서 캐스팅, 웨이브 종료, 대사, 턴 넘기기가 전부 코루틴 안에 있고,
        /// 넷 다 제 조건이 읽히지 않았다고 말하며 도착했다.
        ///
        /// 그것은 읽지 못한 조건이 아니다. 조건이 아니다. 참이어야 했던 것이 없다고 보고하는데, 컴파일러의 고쳐 쓰기가
        /// 아니라 게임을 읽는 사람에게 재개 지점이 뜻하는 바가 그것이기 때문이다 — 진짜 조건은 원래 코드가 쓴 것들이고,
        /// 그것들은 여전히 뒤따르는 블록 안에 있다.
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

                // 또는 같은 분배의 뒤쪽 블록. 첫 블록이 만든 복사본을 검사한다.
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

        /// <summary>코루틴이 어느 yield 에서 멈췄는지를 쥔 필드를 컴파일러가 부르는 이름.</summary>
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
                called.DeclaringType?.FullName != InputType)
            {
                return null;
            }

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

            // 키가 변수 안에 있다. 어느 키인지는 여기서 답할 수 없고, 그렇다고 말하는 것이 이 항목의 값 전부다 —
            // 스캔이 덮지 못하는 입력 하나다.
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
        /// 이 블록에서 일어난 직접 변화. 피호출자의 효과는 피호출자 자신의 근거에 남는다.
        /// </summary>
        /// <remarks>
        /// 직접 효과만 남기는 것은 일부러다. 피호출자의 결과를 여기 복사하면 피호출자의 조건을 잃고, 서로 배타적인 씬
        /// 로드가 동시에 일어나는 것처럼 보이게 된다.
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
        /// 결정이 하는 비교. 이 갈래를 타면 참이 되도록 진술한 것.
        /// </summary>
        /// <remarks>
        /// 분기는 무엇이 제어를 제 대상으로 보내는지를 말한다. 흘러 내려와 도착했다는 것은 그 반대가 성립했다는 뜻이다.
        /// 두 엣지 모두 여기를 지나므로 그중 하나에 대해 검사를 부정한다.
        /// </remarks>
        private static Precondition ReadCondition(
            BasicBlock decision, BasicBlock taken, bool hasThis, MethodDefinition method,
            out string unread)
        {
            var branch = decision.Last;
            unread = null;

            if (!(branch.Operand is Instruction target))
            {
                // switch 다. 어느 case 를 탔는지는 알 수 있지만 피연산자 둘을 읽어서는 아니다.
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

                // 디버그 빌드는 이미 해 둔 비교를 검사하므로, 분기는 그 답이 성립했는지만 말한다. 비교가 곧 조건이다.
                // 그것을 "그 답 != 0" 으로 읽으면 피연산자 둘과 연산자까지 함께 버리게 된다.
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
        /// 이 들어오는 갈래가 switch 의 어느 case 인지.
        /// </summary>
        /// <remarks>
        /// switch 는 통째로 거절돼 왔고 — "알 수 있지만 피연산자 둘을 읽어서는 아니다" — 샘플 게임의 기능 셋이 그
        /// 뒤에 앉아 있었다: 맵이 어떤 배경을 보이는지, 스테이지가 어떤 카드를 주는지, 캐릭터가 어디서 시작하는지.
        /// 점프 테이블은 바로 그 명령어 안에 있고, 할 일은 인덱스가 무엇을 뜻하는지 말하는 것뿐이다.
        ///
        /// 그것을 표 읽기 이상으로 만드는 것이 둘 있다. case 는 다른 case 와 블록을 공유할 수 있어서 (<c>case 4</c> 와
        /// <c>case 5</c> 가 같은 일을 하는 식) 들어오는 갈래 하나가 여러 값을 한꺼번에 뜻할 수 있다 — 검사 하나가
        /// 아니라 선택이다. 그리고 case 가 0 에서 시작하지 않는 switch 는 뺄셈이 앞에 접혀 컴파일되므로 인덱스가 곧
        /// 값이 아니다.
        ///
        /// 흘러 내려가는 갈래는 실제 모습 그대로 비교 한 쌍으로 쓴다. IL 은 부호 없이 비교하므로 음수도 끝을 넘는 값만큼
        /// 확실하게 흘러 내려가고, <c>&gt;= count</c> 만으로는 절반의 숫자에 대해 거짓인 주장이 된다.
        /// </remarks>
        private static Condition SwitchCase(BasicBlock decision, BasicBlock taken, ControlFlowGraph graph)
        {
            if (!(decision.Last.Operand is Instruction[] targets) || targets.Length == 0)
            {
                return Condition.Unreadable("switch");
            }

            var subject = Producer(decision.Last, decision);
            var offset = 0;

            // 0 이 아닌 데서 시작하는 case 를 가진 switch 는 그 이동이 앞에 접혀 있다.
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

            // switch 는 둘을 비교하는 것이 아니라 값 하나를 분기로 보내므로, 다른 조건들이 주어를 어디서 잃었는지 말하는
            // 자리가 아니라 여기서 만든다. 아무 데서도 말하지 않으면 switch 에서 온 `context: null` 은 한 번도 물어진 적
            // 없는 조건처럼 보였다.
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

            // case 중 하나가 아니므로 default 다. 범위의 양 끝이 다 필요하다.
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
        /// 값을 적재해 온 매개변수의 이름.
        /// </summary>
        /// <remarks>
        /// <see cref="IlReading.Describe"/> 는 인자의 이름을 대지 않고, 대개는 대지 않는 것이 맞다 — 인자는 한 메서드
        /// 안에서의 이름이고 호출자의 용어 옆에서는 아무 뜻도 없다. switch 는 그럼에도 가질 값이 있는 경우인데,
        /// 조건 전체가 그 인자이고 그것이 없으면 문장 자체가 없기 때문이다. atom 이 나르는 <c>context</c> 가
        /// <c>arg:N</c> 이라고 말하므로 아무도 그것을 수신자 자신의 상태로 오해할 수 없다.
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
        /// 비교가 비교한 두 값.
        /// </summary>
        /// <remarks>
        /// 오른쪽은 바로 앞에 앉은 것이라 분석이 필요 없다. 왼쪽은 그 아래에 있고, 거기 닿으려면 오른쪽이 소비한 모든
        /// 것을 건너뛰어야 한다 — 아무것도 소비하지 않은 값에 대해서만 명령어 하나 뒤가 슬롯 하나 뒤다. 그것을 명령어
        /// 하나 뒤로 읽으면 오른쪽이 필드이거나 무언가에 대한 호출일 때마다 엉뚱한 피연산자의 이름을 댔다:
        /// <c>a == b.Count</c> 가 <c>b == b.Count</c> 로 나왔다. 건너뛰기를 할 수 없는 자리에서는 왼쪽을 이름 없이 두고
        /// 조건을 읽지 못한 것으로 떨어뜨리는데, 그것이 늘 그랬던 바다.
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

            // 이름이 나온 바로 그 명령어에게, 그것이 찾아볼 자리인지 묻는다. 호출자가 아니라 여기서 하는 것은 왼쪽이
            // 알아내지는 자리가 여기이고, 그것을 다시 찾으려는 두 번째 걷기가 첫 번째와 어긋날 수 있기 때문이다.
            //
            // 필드를 먼저 보고, 안 되면 값을 읽어 온 필드를 본다. `spellCards.Count` 는 호출이 만들어내는 것이라 그 뒤의
            // 목록을 청하기 전까지는 찾아볼 자리가 없다.
            watch = WatchTarget.From(IlReading.Holding(leftAt, method))
                    ?? WatchTarget.ReadOff(leftAt, boundary, method);

            // 이름 붙일 수 없었던 쪽. 읽지 못한 조건의 개수가 무슨 모양에 좌절했는지 말할 수 있도록. 왼쪽이 먼저다:
            // 걷기가 포기하는 쪽이 그쪽이고, 오른쪽은 분기 바로 앞에 앉은 무엇이기 때문이다.
            unreadAt = left == null ? leftAt : (right == null ? rightAt : null);

            // 양쪽이 일치해야 한다. 아니면 문장이 한꺼번에 두 객체에 대한 것이 되고 그것을 고쳐 쓸 대상 하나가 없다.
            // 상수 말고는 아무것에도 뿌리내리지 않은 쪽은 무엇과도 일치하는데, 그것이 평범한 모양이다: `this` 의 필드를
            // 숫자와 비교하는 것.
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
        /// 어떤 명령어가 소비하는 값을 실제로 만들어낸 명령어.
        /// </summary>
        /// <remarks>
        /// 릴리스 빌드는 값을 스택에 남기므로 생산자는 그냥 앞 명령어다. 디버그 빌드는 그러지 않는다: 지역 변수로
        /// 계산해 넣고 곧바로 되읽으며, <c>nop</c> 으로 채워 넣는다. 두 모양 다 같은 소스에 대해 같은 컴파일러에서
        /// 나온다 — 에디터는 최적화해 컴파일하고 개발용 플레이어 빌드는 디버깅용으로 컴파일한다 — 그래서 둘 중 하나만
        /// 아는 독자는 자기가 보고 있지 않은 쪽 빌드를 읽을 수 있는 조건이 하나도 없는 것으로 보고한다.
        ///
        /// 저장은 적재 바로 앞에 앉아 있을 때만 따라간다. 더 뒤에서 대입된 지역 변수는 다른 데서도 대입됐을 수 있고,
        /// 그때 그것을 따라가면 검사되는 그 값이라고 할 수 없는 값의 이름을 대게 된다. 그것이 이 분석이 되도록 내놓지
        /// 않으려는 종류의 틀린 답이라, 대신 멈추고 조건을 읽지 못한 것으로 보고한다.
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

        /// <summary>앞 명령어. 읽고 있는 블록으로 가둔 채.</summary>
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
        /// 디버그 빌드가 분기가 아니라 값으로 남기는 비교.
        /// </summary>
        /// <remarks>
        /// <c>if (a == b)</c> 는 최적화하면 <c>beq</c> 가 되고 아니면 <c>ceq</c> 뒤에 그 결과로 분기하는 것이 된다.
        /// <c>&gt;=</c> 와 <c>&lt;=</c> 는 제 명령어가 없어 그 반대의 부정으로 도착하므로 — <c>clt</c> 다음
        /// <c>ldc.i4.0 ceq</c> — 무언가를 0 과 비교하는 별개의 비교 둘로 보고하는 대신 여기서 부정을 풀어낸다.
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
                    // 리터럴 0 과의 비교가 컴파일러가 "아니다" 를 쓰는 방식이다. 그것이 부정하는 것이 그 자체로 비교일 때, 둘은
                    // 연산자 하나로 접힌다.
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
        /// 메서드로 쓰인 비교. 어떤 타입에는 그것밖에 없다.
        /// </summary>
        /// <remarks>
        /// 문자열 비교, null 과 대조되는 Unity 객체, 제 <c>==</c> 를 가진 구조체 — 이 중 어느 것도 <c>ceq</c> 를
        /// 만들지 않는다. 그것들은 호출로 컴파일되고, 그 호출을 불투명한 값으로 읽는 바람에
        /// <c>name == "GameClearScene"</c> 이 <c>String.op_Equality() != 0</c> 이 됐는데, 그것은 결정되는 것의 어느
        /// 쪽 이름도 대지 않는다.
        ///
        /// 비교 여섯만 취한다. 비교가 아닌 연산자는 결정이 아니라 값을 남기므로, 비교를 대신하는 자리가 아니라 비교의
        /// 한쪽에 속한다.
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
