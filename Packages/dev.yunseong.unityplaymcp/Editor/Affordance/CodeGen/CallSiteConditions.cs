using System.Collections.Generic;
using Mono.Cecil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 한 메서드가 다른 메서드를 부르려면 참이어야 하는 조건.
    /// </summary>
    /// <remarks>
    /// 키 검사는 호출을 지키고 효과는 불린 메서드 안에 있는 경우가 많다. 호출 경로를 따라 조건을 합성하려면 걸음마다
    /// 호출자 쪽 조건이 필요하다. 피호출자의 조건은 피호출자의 수신 객체 기준이라 옮기지 않는다.
    ///
    /// 메서드당 한 번 계산해 캐시한다. 호출 그래프 위쪽 메서드는 여러 경로에서 되풀이해 쓰인다.
    /// </remarks>
    internal sealed class CallSiteConditions
    {
        private readonly ModuleDefinition _module;

        private readonly Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>> _byCaller =
            new Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>>();

        private static readonly Dictionary<MethodDefinition, Site> None =
            new Dictionary<MethodDefinition, Site>();

        /// <summary>
        /// 호출 지점 하나와 그 호출이 호출자 자신의 객체에 대한 것인지 여부.
        /// </summary>
        /// <remarks>
        /// <c>this</c> 에 대해 불린 헬퍼만 호출자와 같은 객체를 말하므로 그 조건을 호출자의 조건과 함께 읽을 수 있다.
        ///
        /// 호출 지점 하나라도 <c>this</c> 가 아니면 거짓이다. 두 방식으로 불리는 메서드의 조건은 어느 쪽 뜻인지 알 수 없다.
        /// </remarks>
        private sealed class Site
        {
            internal Condition When = Condition.Always;
            internal bool OnThis;

            /// <summary>
            /// 호출자 쪽 식으로 쓴 수신 객체.
            /// </summary>
            /// <remarks>
            /// 수신 객체가 둘 이상이거나 호출자가 이름 붙일 수 없으면 null 이다.
            /// </remarks>
            internal string Receiver;

            /// <summary>수신 객체가 `this` 쪽인지 `static` 쪽인지.</summary>
            internal string Where;

            /// <summary>호출자 쪽 식으로 쓴 인자들. <see cref="ArgWhere"/> 는 각 인자의 출처다.</summary>
            internal string[] Args;

            internal string[] ArgWhere;

            internal bool ReceiverKnown;
        }

        internal CallSiteConditions(ModuleDefinition module)
        {
            _module = module;
        }

        /// <summary>호출 지점을 놓지 못해 호출이 조건 없이 닿는 것으로 읽히는 메서드 수.</summary>
        internal int Unplaced { get; private set; }

        /// <summary>
        /// <paramref name="caller"/> 가 <paramref name="callee"/> 에 닿는 조건.
        /// </summary>
        /// <remarks>
        /// 호출 지점을 찾지 못하면 <see cref="Condition.Always"/> 를 돌려준다. 조건을 지어내지 않고 호출자가 그 사실을 따로
        /// 표시한다.
        /// </remarks>
        internal Condition Between(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) ? site.When : Condition.Always;
        }

        /// <summary>
        /// 경로의 모든 걸음이 호출자 자신의 객체에 대한 호출이었는지.
        /// </summary>
        /// <remarks>
        /// 그렇다면 <c>this</c> 가 경로 전체에서 같은 객체이므로 먼 쪽의 <c>this</c> 조건을 진입점 쪽 조건과 이어 붙일 수 있다.
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

        /// <summary>호출 지점 하나를 기록하고, 같은 피호출자에 대한 다른 지점과 합친다.</summary>
        private static void Note(
            Dictionary<MethodDefinition, Site> sites, MethodDefinition callee, Condition guard,
            bool onThis, string receiver, string where, string[] args, string[] argWhere)
        {
            if (sites.TryGetValue(callee, out var already))
            {
                already.When = Condition.Either(new[] { already.When, guard });
                already.OnThis &= onThis;

                // 수신 객체가 둘이면 어느 쪽으로도 말할 수 없다.
                if (already.Receiver != receiver)
                {
                    already.Receiver = null;
                    already.Where = null;
                }

                // 인자가 서로 다르면 어느 쪽으로도 말할 수 없다.
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
        /// 호출자 쪽 식으로 쓴 수신 객체. 하나로 정해지고 호출자 자신의 것일 때만.
        /// </summary>
        /// <remarks>
        /// 지역 변수나 인자로 받은 수신 객체는 호출자 밖에서 이름 붙일 수 없으므로 내놓지 않는다.
        /// </remarks>
        internal string ReceivedOn(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) && site.ReceiverKnown
                ? site.Receiver
                : null;
        }

        /// <summary>
        /// 경로 끝의 메서드가 도는 객체를 진입점 쪽 식으로 쓴 것.
        /// </summary>
        /// <remarks>
        /// 걸음마다 식의 머리를 앞 걸음의 수신 객체 이름으로 바꿔 나간다. `A` 가 `A.zone` 에 대해 `B` 를 부르고 `B` 가
        /// `B.slot` 에 대해 `C` 를 부르면 `C` 는 `A.zone.slot` 위에서 돈다.
        ///
        /// 지역 변수, 인자, 서로 다른 두 수신 객체처럼 한 걸음이라도 옮길 수 없으면 null 이다. 일부만 맞는 식은 엉뚱한
        /// 객체를 가리킨다. 모든 걸음이 `this` 위에 있어 바꿀 것이 없을 때도 null 이다.
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
                    // 같은 객체 위에서 돌므로 진입점 쪽 이름이 그대로 유효하다.
                    continue;
                }

                if (site.Receiver == null)
                {
                    return null;
                }

                // static 뿌리는 어디서든 같은 객체를 가리키므로 나르던 식을 대체한다.
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

        /// <summary>한 메서드가 다른 메서드에 넘긴 인자. 하나로 정해질 때만.</summary>
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

        /// <summary>이 호출이 호출자 자신의 객체에 대한 것인지.</summary>
        private static bool OnThis(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller, Mono.Cecil.Cil.Instruction boundary)
        {
            var reference = call.Operand as MethodReference;

            if (reference == null || !reference.HasThis)
            {
                // static 피호출자는 객체가 없으므로 호출자의 조건과 섞일 수 없다.
                return true;
            }

            // 수신 객체가 `this` 에 속하는 것이 아니라 `this` 자체여야 한다. `this.zone.AddCard()` 의 수신 객체도 `this` 에
            // 속하지만 다른 객체이므로, 그 조건을 호출자의 조건과 섞으면 두 객체를 하나로 읽는다.
            //
            // 블록 경계가 없으면 수신 객체를 읽을 수 없으므로 아니오로 답한다.
            return boundary != null &&
                   IlReading.Receiver(reference, call, boundary, caller) == "this";
        }

        /// <summary>수신 객체의 식. 호출자 자신의 것이거나 static 일 때만.</summary>
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

            // `this` 쪽이나 static 쪽만 받는다. `CardManager.Inst` 같은 static 싱글턴은 어디서든 같은 객체를 가리킨다.
            // 지역 변수나 인자로 받은 수신 객체는 그 메서드 밖에서 이름이 없다.
            var standing = IlReading.ReceiverWhere(reference, call, boundary, caller.HasThis);

            if (standing != "this" && standing != "static")
            {
                return null;
            }

            where = standing;
            return IlReading.Receiver(reference, call, boundary, caller);
        }

        /// <summary>호출자 쪽 식으로 쓴 각 인자와 그 출처.</summary>
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

            // 분기가 없으면 모든 호출이 매번 돈다. 대부분의 메서드가 이 모양이라 그래프를 만들지 않는다.
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
                        // 블록에 호출이 있을 때만 한 번 계산한다.
                        guard = guard ?? VariantBuilder.ReachOf(graph, dependence, block.Index, reached, state);

                        // 서로 다른 조건 아래 두 자리에서 불리면 두 조건의 OR 이 된다.
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
