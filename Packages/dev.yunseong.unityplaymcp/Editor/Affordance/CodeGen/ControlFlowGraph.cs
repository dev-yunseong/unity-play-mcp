using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>맨 위로만 들어오고 맨 아래로만 나가는 명령어의 한 줄기.</summary>
    internal sealed class BasicBlock
    {
        internal int Index;
        internal Instruction First;
        internal Instruction Last;
        internal bool IsExit;

        internal readonly List<BasicBlock> Successors = new List<BasicBlock>();
        internal readonly List<BasicBlock> Predecessors = new List<BasicBlock>();

        /// <summary>이 블록을 떠나는 일이 걸음이 아니라 결정일 때 참.</summary>
        internal bool IsDecision => Successors.Count > 1;
    }

    /// <summary>
    /// 한 메서드의 모양. 블록들과 그 사이의 길들로 본 것.
    /// </summary>
    /// <remarks>
    /// 뒤의 모든 것이 경로를 묻는다 — 어떤 키가 어떤 코드를 지키는가, 어디에 닿기 위해 무엇이 참이어야
    /// 했는가 — 그리고 경로에 대한 물음은 명령어를 쓰인 순서대로 읽어서는 답할 수 없다. <c>||</c> 를 품은
    /// <c>if/else</c> 사슬은 한 키가 지키는 본문을 다른 키의 검사들 사이에 물리적으로 끼워 넣는다.
    /// </remarks>
    internal sealed class ControlFlowGraph
    {
        /// <summary>
        /// 이 크기를 넘으면 메서드를 건드리지 않는다.
        /// </summary>
        /// <remarks>
        /// 생성된 코드는 사람이 손으로 쓰지 않는 크기로 도착한다. 위쪽의 크기 필터가 이미 최악의 것들을
        /// 돌려보내고, 이것은 거기를 빠져나온 것에 대한 마지막 방벽이다.
        /// </remarks>
        internal const int MaxBlocks = 2000;

        internal List<BasicBlock> Blocks { get; private set; }
        internal BasicBlock Entry { get; private set; }

        /// <summary>이 메서드에 수신자가 있는지. ldarg.0 을 가려내기 위한 것.</summary>
        internal bool HasThis { get; private set; }

        /// <summary>이것이 그 모양인 메서드. 제 인자들에 이름을 붙이기 위해 쥔다.</summary>
        internal MethodDefinition Method { get; private set; }

        /// <summary>
        /// 코루틴이 재개 지점을 복사해 넣는 지역 변수, 또는 -1.
        /// </summary>
        /// <remarks>
        /// <c>MoveNext</c> 맨 위의 분배는 state 필드를 한 번 읽고 그 복사본으로 분기하는데, 때로는 여러 번
        /// 그렇게 한다. 그 블록들 중 필드를 언급하는 것은 첫 번째뿐이고, 나머지는 여느 것과 다를 바 없어
        /// 보이는 지역 변수를 검사한다. 그것이 어느 지역 변수인지를 알면 전부가 원래 무엇이었는지로 돌아온다.
        /// </remarks>
        internal int StateSlot { get; private set; } = -1;

        /// <summary>
        /// 모든 return 과 throw 가 도착하는 자리.
        /// </summary>
        /// <remarks>
        /// 합성된 것이고 명령어를 하나도 담지 않는다. post-dominance 는 끝점 하나에서 묻는 것인데,
        /// <c>return</c> 이 셋인 메서드는 이것 없이는 끝점이 셋이다.
        /// </remarks>
        internal BasicBlock Exit { get; private set; }

        /// <summary>메서드가 너무 커서 그래프를 만들지 못했고 여기 있는 것 중 쓸 수 있는 게 없을 때 참.</summary>
        internal bool Abandoned { get; private set; }

        /// <summary>코루틴의 state 필드가 복사돼 들어간 지역 변수. 있다면.</summary>
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

        /// <summary>진입만 가능한, 그래서 블록을 시작해야 하는 명령어들.</summary>
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

            // region 경계도 블록을 시작한다. 핸들러로 들고 나는 길은 아래에서 모델링하지 않지만, 블록이 그
            // 경계를 걸치게 두면 함께 돌지 않는 명령어들이 함께 도는 줄기 안에 들어가 버린다.
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
                // 본문 끝을 지나쳐 달린다. 잘못된 것이지만 남의 어셈블리다.
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
            // switch 는 같은 대상을 두 번 적을 수 있다. 그것을 같은 짝 사이의 두 길로 세면 그 뒤의 모든 개수가
            // 그런 일이 일어난 횟수만큼 어긋난다.
            if (from.Successors.Contains(to))
            {
                return;
            }

            from.Successors.Add(to);
            to.Predecessors.Add(from);
        }
    }
}
