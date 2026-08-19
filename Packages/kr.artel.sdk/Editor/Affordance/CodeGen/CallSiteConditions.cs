using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 한 메서드가 다른 메서드를 부르기 위해 무엇이 참이어야 했는가.
    /// </summary>
    /// <remarks>
    /// 플레이어가 누르는 키와 그것이 일으키는 변화가 같은 메서드에 있는 일은 드물다: 키 검사가 호출을
    /// 지키고, 씬 로드는 불린 메서드 안에 앉아 있다. 한 번에 한 메서드씩 읽으면 입력과 결과는 서로를 한
    /// 번도 언급하지 않는 두 기록이다.
    ///
    /// 이것이 그 둘을 잇는 절반이다. 호출 경로를 따라 합성하려면 각 걸음의 조건이 필요하고, 그 조건은
    /// 호출자의 것이다 — 호출자 자신의 용어로 쓰였고 앞으로 날라도 참으로 남는다. 여기서는 피호출자의
    /// 조건을 건드리지 않는다. 그것은 피호출자의 수신자에 대고 쓰였으므로 옮기면 다른 말을 한다.
    ///
    /// 메서드당 한 번 알아내고 쥐고 있는다. 호출 그래프 위쪽의 메서드는 나머지 대부분으로 가는 길목에 있다.
    /// </remarks>
    internal sealed class CallSiteConditions
    {
        private readonly ModuleDefinition _module;

        private readonly Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>> _byCaller =
            new Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>>();

        private static readonly Dictionary<MethodDefinition, Site> None =
            new Dictionary<MethodDefinition, Site>();

        /// <summary>
        /// 호출 하나, 그리고 그것이 호출자 자신의 객체에 대고 이루어졌는지.
        /// </summary>
        /// <remarks>
        /// 조건은 합성되는 절반이다. 나머지 절반은 그 호출이 누구에 대한 것이었는가다: <c>this</c> 에 대고
        /// 불린 헬퍼는 제 호출자와 같은 객체를 말하고 있으므로 그 조건을 호출자의 조건 옆에서 읽을 수 있다.
        /// 다른 무엇에 대고 불렸다면 그럴 수 없다.
        ///
        /// 호출 지점 하나라도 <c>this</c> 가 아니면 곧바로 거짓이다. 경로는 그 전부를 대표하기 때문이다. 두
        /// 방식으로 불리는 메서드는 그 조건이 둘 중 어느 쪽 뜻도 될 수 있는 메서드다.
        /// </remarks>
        private sealed class Site
        {
            internal Condition When = Condition.Always;
            internal bool OnThis;

            /// <summary>
            /// 호출자가 그것을 무엇에 대고 불렀는가. 호출자 자신의 말로.
            /// </summary>
            /// <remarks>
            /// 호출이 둘 이상에 대고 이루어졌거나 호출자가 이름 붙일 수 없는 무언가에 대고 이루어졌을 때 null.
            /// 서로 다른 수신자 둘은 서로 다른 객체 둘이고, 그 둘 다인 표현식은 없다.
            /// </remarks>
            internal string Receiver;

            /// <summary>수신자가 누구의 것인지 — `this` 인지 `static` 인지.</summary>
            internal string Where;

            /// <summary>무엇이 넘어갔는지, 호출자의 말로, 그리고 각각이 누구의 것인지.</summary>
            internal string[] Args;

            internal string[] ArgWhere;

            internal bool ReceiverKnown;
        }

        internal CallSiteConditions(ModuleDefinition module)
        {
            _module = module;
        }

        /// <summary>호출 지점을 놓지 못해 그 호출이 지켜지지 않은 것으로 읽히는 메서드들.</summary>
        internal int Unplaced { get; private set; }

        /// <summary>
        /// <paramref name="caller"/> 가 <paramref name="callee"/> 에 닿는 조건.
        /// </summary>
        /// <remarks>
        /// 호출이 아무것에도 지켜지지 않을 때는 언제나이고, 그것이 흔한 경우이면서 안전한 답이다: 놓지 못한
        /// 조건은 합성된 경로를 조건 없이 닿을 수 있는 것으로 읽히게 만들고, 호출자는 파수꾼을 지어내는 대신
        /// 그 사실을 표시한다.
        /// </remarks>
        internal Condition Between(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) ? site.When : Condition.Always;
        }

        /// <summary>
        /// 경로의 모든 걸음이 호출자가 제 객체에 대고 한 호출이었는지.
        /// </summary>
        /// <remarks>
        /// 그랬다면 <c>this</c> 는 경로의 이 끝에서 저 끝까지 같은 객체이고, 먼 쪽 끝에서 <c>this</c> 에 대고
        /// 쓰인 조건은 가까운 쪽 끝에서도 같은 말을 한다. 두 조건을 한 문장으로 이어 붙여도 문장의 뜻이 바뀌지
        /// 않는 유일한 경우가 그것이다.
        /// </remarks>
        internal bool StaysOnThis(IReadOnlyList<MethodDefinition> path)
        {
            if (path == null || path.Count < 2)
            {
                return true;
            }

            for (var index = 0; index + 1 < path.Count; index++)
            {
                if (!SitesIn(path[index]).TryGetValue(path[index + 1], out var site) || !site.OnThis)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>경로를 따라 조건들을 순서대로 합성한다.</summary>
        internal Condition Along(IReadOnlyList<MethodDefinition> path)
        {
            if (path == null || path.Count < 2)
            {
                return Condition.Always;
            }

            var steps = new List<Condition>(path.Count - 1);

            for (var index = 0; index + 1 < path.Count; index++)
            {
                steps.Add(Between(path[index], path[index + 1]));
            }

            return Condition.Every(steps);
        }

        /// <summary>호출 지점 하나를 기록하고, 같은 피호출자에 대한 다른 지점과 잇는다.</summary>
        private static void Note(
            Dictionary<MethodDefinition, Site> sites, MethodDefinition callee, Condition guard,
            bool onThis, string receiver, string where, string[] args, string[] argWhere)
        {
            if (sites.TryGetValue(callee, out var already))
            {
                already.When = Condition.Either(new[] { already.When, guard });
                already.OnThis &= onThis;

                // 둘에 대고 불렸다는 것은 딱히 어느 쪽에 대고도 불리지 않았다는 것이다.
                if (already.Receiver != receiver)
                {
                    already.Receiver = null;
                    already.Where = null;
                }

                // 서로 다른 둘을 가지고 불렸다는 것은 딱히 어느 쪽을 가지고도 불리지 않았다는 것이다.
                if (!Same(already.Args, args))
                {
                    already.Args = null;
                    already.ArgWhere = null;
                }

                return;
            }

            sites[callee] = new Site
            {
                When = guard, OnThis = onThis, Receiver = receiver, Where = where,
                Args = args, ArgWhere = argWhere, ReceiverKnown = true
            };
        }

        /// <summary>
        /// 한 메서드가 다른 메서드를 무엇에 대고 불렀는가. 답이 하나이고 그것이 호출자 자신의 것일 때.
        /// </summary>
        /// <remarks>
        /// 표현식은 호출자의 말로 되어 있으므로, 그것은 호출이 쓰인 자리에서 피호출자의 <c>this</c> 가 불리는
        /// 이름이다. 그 주어가 호출자의 <c>this</c> 일 때만 내놓는다 — 지역 변수에 쥐고 있거나 인자로 건네받은
        /// 수신자는 호출자가 다른 누구에게도 이름 붙여 줄 수 없는 것이다.
        /// </remarks>
        internal string ReceivedOn(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) && site.ReceiverKnown
                ? site.Receiver
                : null;
        }

        /// <summary>
        /// 경로의 먼 쪽 끝이 무엇 위에서 돌고 있는가를, 가까운 쪽 끝이 선 자리에서 말한 것.
        /// </summary>
        /// <remarks>
        /// 한 걸음이면 호출자가 쓴 수신자다. 두 걸음이면 첫 피호출자의 말로 쓰인 둘째 수신자인데, 그 말은
        /// 진입점에서 아무 뜻도 없다 — 그래서 한 걸음씩 되날라 오면서 표현식의 머리를 그 앞 걸음이 그 객체를
        /// 부르던 이름으로 갈아 끼운다. `A` 가 `A.zone` 에 대고 `B` 를 부르고 `B` 가 `B.slot` 에 대고 `C` 를
        /// 부르면, `C` 는 `A.zone.slot` 위에서 돈다.
        ///
        /// 한 걸음이라도 나를 수 없으면 그 순간 null 이다: 지역 변수에 대한 호출, 인자에 대한 호출, 두 가지로
        /// 다르게 불린 것에 대한 호출. 사슬 전체가 버텨야 한다. 마지막 세 걸음에 대해 맞고 첫 걸음에 대해 틀린
        /// 표현식은 완전한 확신을 가지고 엉뚱한 객체의 이름을 대기 때문이다.
        ///
        /// 아무것도 움직이지 않았을 때도 null 이다 — 모든 걸음이 호출자 자신의 객체 위에 있으면 `this` 는
        /// 뜻하던 것을 계속 뜻하고, 고쳐 쓸 것이 없다.
        /// </remarks>
        internal string ReceivedAlong(IReadOnlyList<MethodDefinition> path, out string where)
        {
            where = null;

            if (path == null || path.Count < 2)
            {
                return null;
            }

            string expression = null;

            for (var index = 0; index + 1 < path.Count; index++)
            {
                var caller = path[index];

                if (!SitesIn(caller).TryGetValue(path[index + 1], out var site))
                {
                    return null;
                }

                if (site.OnThis)
                {
                    // 피호출자는 호출자가 있던 것과 같은 객체 위에서 돌고 있으므로, 진입점에서 그것을 부르던 이름이
                    // 여전히 그 이름이다.
                    continue;
                }

                if (site.Receiver == null)
                {
                    return null;
                }

                // static 뿌리는 어디서든 제 객체의 이름을 대므로, 나르던 것에 매달리는 대신 그것을 갈아치운다.
                if (site.Where == "static" || expression == null)
                {
                    expression = site.Receiver;
                    where = site.Where;
                    continue;
                }

                expression = Condition.Swapped(site.Receiver, caller.DeclaringType?.Name, expression);

                if (expression == null)
                {
                    where = null;
                    return null;
                }
            }

            return expression;
        }

        private static bool Same(string[] left, string[] right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>한 메서드가 다른 메서드에 무엇을 넘겼는가. 답이 하나일 때.</summary>
        internal void PassedOn(
            MethodDefinition caller, MethodDefinition callee, out string[] args, out string[] argWhere)
        {
            args = null;
            argWhere = null;

            if (SitesIn(caller).TryGetValue(callee, out var site))
            {
                args = site.Args;
                argWhere = site.ArgWhere;
            }
        }

        /// <summary>이 호출이 호출자 자신의 객체에 대고 이루어졌는지.</summary>
        private static bool OnThis(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller, Mono.Cecil.Cil.Instruction boundary)
        {
            var reference = call.Operand as MethodReference;

            if (reference == null || !reference.HasThis)
            {
                // static 피호출자는 제 객체가 없으므로 호출자의 무엇도 그것으로 오인될 수 없다.
                return true;
            }

            // 수신자가 `this` 에 속하는 것이 아니라 `this` *여야* 한다. 수신자가 누구의 것이냐고 물으면
            // `this.zone.AddCard()` 도 `this.AddCard()` 만큼이나 쉽게 "this" 라고 답하고 — `this` 의 필드는
            // `this` 에 대한 것이므로 — 그 답 위에서 피호출자의 조건이 한 객체의 진술로서 호출자의 문장 안에
            // 섞여 들어갔다. 그것들은 두 객체다: 샘플 게임은 `this.combineZone` 에 대고 `AddCard` 를 불러 카드를
            // 내려놓는데, 합성된 기록은 `context: this` 와 함께 `CombineZone.spellCards.Count == 1` 이라고
            // 말했고 거기서 `this` 는 카드다.
            //
            // 수신자에 이름을 붙이는 일은 최근에야 가능해졌고, 누구의 것이냐를 말하는 방식은 그 전에 있던 것이다.
            // 이제 표현식을 읽을 수 있으므로 읽는다.
            //
            // 걷기를 가둘 블록이 없으면 정직한 답이 없고, 답이 없을 때의 정직한 답은 아니오다.
            return boundary != null &&
                   IlReading.Receiver(reference, call, boundary, caller) == "this";
        }

        /// <summary>수신자의 표현식. 그것이 호출자 자신의 것 중 하나일 때.</summary>
        private static string ReceiverAt(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller, Mono.Cecil.Cil.Instruction boundary,
            out string where)
        {
            where = null;
            var reference = call.Operand as MethodReference;

            if (reference == null || !reference.HasThis || boundary == null)
            {
                return null;
            }

            // 호출자 자신의 것이거나, 스스로 서는 것. static 을 거쳐 닿는 싱글턴은 — `CardManager.Inst` —
            // 어디서든 같은 객체의 이름을 대므로 호출자의 것이 해내는 것보다 크다. 지역 변수에 쥐고 있거나 인자로
            // 건네받은 수신자는 그것이 쓰인 메서드 밖에서는 아무 이름도 대지 못한다.
            var standing = IlReading.ReceiverWhere(reference, call, boundary, caller.HasThis);

            if (standing != "this" && standing != "static")
            {
                return null;
            }

            where = standing;
            return IlReading.Receiver(reference, call, boundary, caller);
        }

        /// <summary>각 인자가 무엇이었는지, 호출자의 말로, 그리고 각각이 누구의 것인지.</summary>
        private static string[] PassedAt(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller,
            Mono.Cecil.Cil.Instruction boundary, out string[] whose)
        {
            whose = null;
            var reference = call.Operand as MethodReference;
            var count = reference?.Parameters.Count ?? 0;

            if (count == 0 || boundary == null)
            {
                return null;
            }

            var terms = new string[count];
            whose = new string[count];
            var read = false;

            for (var index = 0; index < count; index++)
            {
                var at = IlReading.ArgumentFrom(reference, call, boundary, index);

                if (at == null)
                {
                    continue;
                }

                terms[index] = IlReading.Describe(at, boundary, caller);
                whose[index] = IlReading.Where(at, boundary, caller.HasThis, caller, out _);
                read |= terms[index] != null;
            }

            return read ? terms : null;
        }

        private Dictionary<MethodDefinition, Site> SitesIn(MethodDefinition caller)
        {
            if (_byCaller.TryGetValue(caller, out var known))
            {
                return known;
            }

            var sites = Build(caller);
            _byCaller[caller] = sites;
            return sites;
        }

        private Dictionary<MethodDefinition, Site> Build(MethodDefinition caller)
        {
            if (!caller.HasBody || AnalysisScope.IsTooLarge(caller))
            {
                return None;
            }

            var sites = new Dictionary<MethodDefinition, Site>();

            // 본문에 결정이 없다는 것은 그 안의 모든 호출이 메서드가 돌 때마다 돈다는 뜻이다. 제 경로를 둘 값이
            // 있는 것은 대부분의 메서드가 이 모양이고 그것들에 대해 그래프를 만드는 일이 바로 scope 필터가 피하려고
            // 존재하는 일이기 때문이다.
            if (!AnalysisScope.NeedsControlFlow(caller))
            {
                foreach (var instruction in caller.Body.Instructions)
                {
                    var callee = CallGraph.CalleeAt(instruction, _module);

                    if (callee != null)
                    {
                        Note(sites, callee, Condition.Always, OnThis(instruction, caller, null),
                            ReceiverAt(instruction, caller, null, out var standing), standing,
                            null, null);
                    }
                }

                return sites;
            }

            var graph = ControlFlowGraph.Build(caller.Body);

            if (graph == null || graph.Abandoned)
            {
                Unplaced++;
                return None;
            }

            var dependence = ControlDependence.Compute(graph);
            var reached = new Condition[graph.Blocks.Count];
            var state = new byte[graph.Blocks.Count];

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                Condition guard = null;

                for (var instruction = block.First; instruction != null; instruction = instruction.Next)
                {
                    var callee = CallGraph.CalleeAt(instruction, _module);

                    if (callee != null)
                    {
                        // 블록에 호출이 있다는 것이 드러난 뒤에야 한 번 알아낸다.
                        guard = guard ?? VariantBuilder.ReachOf(graph, dependence, block.Index, reached, state);

                        // 서로 다른 조건 아래 두 자리에서 불렸다는 것은 둘 중 아무거나면 된다는 뜻이고, 그것이 곧 대안이다.
                        Note(sites, callee, guard, OnThis(instruction, caller, block.First),
                            ReceiverAt(instruction, caller, block.First, out var standing), standing,
                            PassedAt(instruction, caller, block.First, out var whose), whose);
                    }

                    if (instruction == block.Last)
                    {
                        break;
                    }
                }
            }

            return sites;
        }
    }
}
