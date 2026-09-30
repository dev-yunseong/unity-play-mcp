using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>맨 위로만 들어오고 맨 아래로만 나가는 명령어 묶음.</summary>
    internal sealed class BasicBlock
    {
        internal int Index;
        internal Instruction First;
        internal Instruction Last;
        internal bool IsExit;

        internal readonly List<BasicBlock> Successors = new List<BasicBlock>();
        internal readonly List<BasicBlock> Predecessors = new List<BasicBlock>();

        /// <summary>블록의 끝이 조건 분기일 때 참.</summary>
        internal bool IsDecision => Successors.Count > 1;
    }

    /// <summary>
    /// 한 메서드의 블록과 그 사이의 엣지.
    /// </summary>
    /// <remarks>
    /// 뒤 단계는 모두 경로를 묻는데, 명령어를 순서대로 읽어서는 답할 수 없다. <c>||</c> 가 낀 <c>if/else</c> 사슬은
    /// 한 키가 지키는 본문을 다른 키의 검사 사이에 끼워 넣는다.
    /// </remarks>
    internal sealed class ControlFlowGraph
    {
        /// <summary>
        /// 이 블록 수를 넘으면 메서드를 건드리지 않는다.
        /// </summary>
        /// <remarks>
        /// 앞의 명령어 수 필터를 빠져나온 생성 코드에 대한 마지막 방어선이다.
        /// </remarks>
        internal const int MaxBlocks = 2000;

        internal List<BasicBlock> Blocks { get; private set; }
        internal BasicBlock Entry { get; private set; }

        /// <summary>메서드에 수신 객체가 있는지. ldarg.0 을 구분하는 데 쓴다.</summary>
        internal bool HasThis { get; private set; }

        /// <summary>이 그래프의 메서드. 인자 이름을 붙이는 데 쓴다.</summary>
        internal MethodDefinition Method { get; private set; }

        /// <summary>
        /// coroutine 이 재개 지점(state 필드)을 복사해 넣는 지역 변수. 없으면 -1.
        /// </summary>
        /// <remarks>
        /// <c>MoveNext</c> 맨 위의 분배는 state 필드를 한 번 읽고 그 복사본으로 여러 번 분기한다. 첫 블록만 필드를 언급하므로
        /// 이 지역 변수를 알아야 나머지 분기도 state 검사로 읽을 수 있다.
        /// </remarks>
        internal int StateSlot { get; private set; } = -1;

        /// <summary>
        /// 모든 return 과 throw 가 도착하는 합성 블록.
        /// </summary>
        /// <remarks>
        /// 명령어를 담지 않는다. post-dominance 는 끝점 하나를 기준으로 하므로 <c>return</c> 이 여럿인 메서드에 필요하다.
        /// </remarks>
        internal BasicBlock Exit { get; private set; }

        /// <summary>메서드가 너무 커서 그래프를 만들지 못했을 때 참. 이때 나머지 값은 쓸 수 없다.</summary>
        internal bool Abandoned { get; private set; }

        /// <summary>coroutine 의 state 필드가 복사된 지역 변수 번호. 없으면 -1.</summary>
        private static int ResumeSlot(Mono.Collections.Generic.Collection<Instruction> instructions)
        {
            for (var index = 0; index + 1 < instructions.Count; index++)
            {
                var load = instructions[index];

                if (load.OpCode.Code != Code.Ldfld ||
                    !(load.Operand is FieldReference field) ||
                    field.Name != "<>1__state")
                {
                    continue;
                }

                switch (instructions[index + 1].OpCode.Code)
                {
                    case Code.Stloc_0: return 0;
                    case Code.Stloc_1: return 1;
                    case Code.Stloc_2: return 2;
                    case Code.Stloc_3: return 3;
                    case Code.Stloc:
                    case Code.Stloc_S:
                        return (instructions[index + 1].Operand as VariableReference)?.Index ?? -1;
                }
            }

            return -1;
        }

        internal static ControlFlowGraph Build(MethodBody body)
        {
            var instructions = body.Instructions;
            if (instructions.Count == 0)
            {
                return null;
            }

            var leaders = FindLeaders(body);
            var graph = new ControlFlowGraph
            {
                Blocks = new List<BasicBlock>(),
                HasThis = body.Method != null && body.Method.HasThis,
                StateSlot = ResumeSlot(instructions),
                Method = body.Method
            };

            var blockByFirst = new Dictionary<Instruction, BasicBlock>();
            BasicBlock current = null;

            foreach (var instruction in instructions)
            {
                if (current == null || leaders.Contains(instruction))
                {
                    if (graph.Blocks.Count >= MaxBlocks)
                    {
                        graph.Abandoned = true;
                        return graph;
                    }

                    current = new BasicBlock { Index = graph.Blocks.Count, First = instruction };
                    graph.Blocks.Add(current);
                    blockByFirst[instruction] = current;
                }

                current.Last = instruction;
            }

            graph.Entry = graph.Blocks[0];
            graph.Exit = new BasicBlock { Index = graph.Blocks.Count, IsExit = true };
            graph.Blocks.Add(graph.Exit);

            graph.Connect(blockByFirst);
            return graph;
        }

        /// <summary>블록을 시작해야 하는 명령어들.</summary>
        private static HashSet<Instruction> FindLeaders(MethodBody body)
        {
            var leaders = new HashSet<Instruction>();
            var instructions = body.Instructions;
            leaders.Add(instructions[0]);

            foreach (var instruction in instructions)
            {
                switch (instruction.OpCode.FlowControl)
                {
                    case FlowControl.Branch:
                    case FlowControl.Cond_Branch:
                        AddTargets(leaders, instruction);
                        AddNext(leaders, instruction);
                        break;

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        AddNext(leaders, instruction);
                        break;
                }
            }

            // region 경계도 블록을 시작한다. 핸들러 경로는 모델링하지 않지만, 블록이 경계를 걸치면 함께 돌지 않는
            // 명령어가 한 블록에 들어간다.
            foreach (var handler in body.ExceptionHandlers)
            {
                AddIfPresent(leaders, handler.TryStart);
                AddIfPresent(leaders, handler.TryEnd);
                AddIfPresent(leaders, handler.HandlerStart);
                AddIfPresent(leaders, handler.HandlerEnd);
                AddIfPresent(leaders, handler.FilterStart);
            }

            return leaders;
        }

        private static void AddTargets(HashSet<Instruction> leaders, Instruction instruction)
        {
            if (instruction.Operand is Instruction target)
            {
                leaders.Add(target);
                return;
            }

            if (instruction.Operand is Instruction[] targets)
            {
                foreach (var each in targets)
                {
                    AddIfPresent(leaders, each);
                }
            }
        }

        private static void AddNext(HashSet<Instruction> leaders, Instruction instruction)
        {
            AddIfPresent(leaders, instruction.Next);
        }

        private static void AddIfPresent(HashSet<Instruction> leaders, Instruction instruction)
        {
            if (instruction != null)
            {
                leaders.Add(instruction);
            }
        }

        private void Connect(Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            foreach (var block in Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                var last = block.Last;

                switch (last.OpCode.FlowControl)
                {
                    case FlowControl.Branch:
                        LinkToTargets(block, last, blockByFirst);
                        break;

                    case FlowControl.Cond_Branch:
                        LinkToTargets(block, last, blockByFirst);
                        LinkToNext(block, last, blockByFirst);
                        break;

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        Link(block, Exit);
                        break;

                    default:
                        LinkToNext(block, last, blockByFirst);
                        break;
                }
            }
        }

        private void LinkToTargets(BasicBlock from, Instruction last, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (last.Operand is Instruction target)
            {
                LinkTo(from, target, blockByFirst);
                return;
            }

            if (last.Operand is Instruction[] targets)
            {
                foreach (var each in targets)
                {
                    LinkTo(from, each, blockByFirst);
                }
            }
        }

        private void LinkToNext(BasicBlock from, Instruction last, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (last.Next == null)
            {
                // 본문 끝을 지나친다. 잘못된 IL 이지만 남의 어셈블리이므로 견딘다.
                Link(from, Exit);
                return;
            }

            LinkTo(from, last.Next, blockByFirst);
        }

        private void LinkTo(BasicBlock from, Instruction target, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (target != null && blockByFirst.TryGetValue(target, out var to))
            {
                Link(from, to);
            }
        }

        private static void Link(BasicBlock from, BasicBlock to)
        {
            // switch 는 같은 대상을 두 번 적을 수 있다. 엣지 둘로 세면 뒤의 모든 개수가 어긋난다.
            if (from.Successors.Contains(to))
            {
                return;
            }

            from.Successors.Add(to);
            to.Predecessors.Add(from);
        }
    }
}
