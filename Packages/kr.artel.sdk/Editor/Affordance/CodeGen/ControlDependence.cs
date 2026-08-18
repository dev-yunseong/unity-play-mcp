using System.Collections.Generic;

namespace Artel.Affordances.CodeGen
{
    /// <summary>결정 하나와, 거기 닿기 위해 그 결정의 어느 갈래를 탔는가.</summary>
    /// <remarks>
    /// 어느 갈래인지는 어느 결정인지만큼 중요하다. 같은 비교가 한쪽 엣지에서는 <c>StagePosition &gt;= 1</c>
    /// 로 읽히고 다른 쪽에서는 <c>&lt; 1</c> 로 읽힌다. 결정만 기억하는 모델은 무엇이 참이어야 했는지를
    /// 말하는 절반을 잃은 것이다.
    /// </remarks>
    internal readonly struct Governor
    {
        internal readonly int Decision;
        internal readonly int Taken;

        internal Governor(int decision, int taken)
        {
            Decision = decision;
            Taken = taken;
        }
    }

    /// <summary>
    /// 각 블록이 어떤 결정들에 매여 있는가.
    /// </summary>
    /// <remarks>
    /// 뒤에서 중요해지는 물음은 둘이고 둘 다 경로에 대한 것이다: 어떤 키가 지키는 코드란 그 키의 분기에
    /// control dependent 한 블록들이고, 어딘가에 닿기 위한 선행 조건이란 그 블록이 control dependent 한
    /// 결정들이다. 이렇게 물으면 <c>A || B</c> 에 특별한 처리가 필요 없다 — 단락 평가는 그저 또 하나의
    /// 엣지다.
    ///
    /// post-dominance 가 먼저인 것은 control dependence 가 그것으로 정의되기 때문이다: B 가 도는지를 A 가
    /// 고를 수 있을 때 B 는 A 에 control dependent 하고, 이는 곧 B 가 A 를 post-dominate 하지 않으면서
    /// A 에서 나가는 경로 위에 있다는 뜻이다.
    /// </remarks>
    internal sealed class ControlDependence
    {
        /// <summary>
        /// 그래프를 부적합으로 선언하기 전까지 고정점 루프가 돌 수 있는 횟수.
        /// </summary>
        /// <remarks>
        /// 제대로 된 입력이면 루프는 몇 번 만에 수렴한다. 이만큼 헐거운 한계에 걸리는 것은 애초에 가라앉을 리
        /// 없던 입력뿐이다.
        /// </remarks>
        private const int MaxPasses = 200;

        private readonly ControlFlowGraph _graph;
        private readonly BasicBlock[] _immediatePostDominator;
        private readonly int[] _reverseOrder;
        private readonly bool[] _reachesExit;
        private readonly List<Governor>[] _dependsOn;

        /// <summary>어느 경로로도 exit 에 닿지 못해 빼 둔 블록들.</summary>
        internal int StrandedBlocks { get; private set; }

        /// <summary>한계에 닿아 답이 불완전할 때 참.</summary>
        internal bool HitLimit { get; private set; }

        internal int DecisionCount { get; private set; }
        internal int DependenceCount { get; private set; }

        private ControlDependence(ControlFlowGraph graph)
        {
            _graph = graph;
            var count = graph.Blocks.Count;
            _immediatePostDominator = new BasicBlock[count];
            _reverseOrder = new int[count];
            _reachesExit = new bool[count];
            _dependsOn = new List<Governor>[count];
        }

        internal static ControlDependence Compute(ControlFlowGraph graph)
        {
            var dependence = new ControlDependence(graph);
            dependence.Run();
            return dependence;
        }

        /// <summary>이 블록이 매여 있는 결정들.</summary>
        internal IReadOnlyList<Governor> Governing(int blockIndex)
        {
            return _dependsOn[blockIndex] ?? (IReadOnlyList<Governor>)System.Array.Empty<Governor>();
        }

        private void Run()
        {
            var order = OrderFromExit();
            ComputePostDominators(order);
            ComputeDependence();
        }

        /// <summary>
        /// exit 에 닿을 수 있는 블록들. exit 에 가까운 것부터.
        /// </summary>
        /// <remarks>
        /// 전체를 유한하게 유지하는 것이 이 단계이고, 이것을 빼놓은 것이 에디터를 얼려서 왜 그런지 알아보려
        /// 열 수조차 없게 만든 원인이다. post-dominance 는 exit 로 가는 경로가 있는 블록에 대해서만 정의된다.
        /// 그런 경로가 없는 블록은 — 끝나지 않는 루프, 컴파일러가 남긴 코드 — immediate post-dominator 가
        /// 없고, 그런 블록 둘을 비교하면 어느 쪽에도 없는 부모 사슬을 영영 만나지 못한 채 걷게 된다.
        ///
        /// 비교에 한계를 두면 회전은 멎는다. 시작하지 않는 편이 낫다: 그 블록들에 대한 답은 존재하지 않으므로
        /// 추측하는 대신 세어서 옆으로 치워 둔다.
        /// </remarks>
        private List<BasicBlock> OrderFromExit()
        {
            var postOrder = new List<BasicBlock>();
            var visited = new bool[_graph.Blocks.Count];
            var nodes = new Stack<BasicBlock>();
            var nextEdge = new Stack<int>();

            nodes.Push(_graph.Exit);
            nextEdge.Push(0);
            visited[_graph.Exit.Index] = true;

            // 명시적 스택으로 걷는다. 문제가 될 만큼 깊은 메서드는 재귀 걷기를 넘치게 할 만큼 깊고, 그 실패는
            // 죽은 에디터로 도착한다.
            while (nodes.Count > 0)
            {
                var node = nodes.Peek();
                var edge = nextEdge.Pop();

                if (edge < node.Predecessors.Count)
                {
                    nextEdge.Push(edge + 1);
                    var previous = node.Predecessors[edge];

                    if (!visited[previous.Index])
                    {
                        visited[previous.Index] = true;
                        nodes.Push(previous);
                        nextEdge.Push(0);
                    }

                    continue;
                }

                postOrder.Add(node);
                nodes.Pop();
            }

            foreach (var block in _graph.Blocks)
            {
                if (!visited[block.Index])
                {
                    StrandedBlocks++;
                }
            }

            foreach (var block in postOrder)
            {
                _reachesExit[block.Index] = true;
            }

            postOrder.Reverse();

            for (var position = 0; position < postOrder.Count; position++)
            {
                _reverseOrder[postOrder[position].Index] = position;
            }

            return postOrder;
        }

        private void ComputePostDominators(List<BasicBlock> order)
        {
            _immediatePostDominator[_graph.Exit.Index] = _graph.Exit;

            var changed = true;
            var passes = 0;

            while (changed)
            {
                if (passes++ >= MaxPasses)
                {
                    HitLimit = true;
                    return;
                }

                changed = false;

                foreach (var block in order)
                {
                    if (block.IsExit)
                    {
                        continue;
                    }

                    BasicBlock candidate = null;

                    foreach (var successor in block.Successors)
                    {
                        if (!_reachesExit[successor.Index] || _immediatePostDominator[successor.Index] == null)
                        {
                            continue;
                        }

                        candidate = candidate == null ? successor : Intersect(successor, candidate);

                        if (candidate == null)
                        {
                            break;
                        }
                    }

                    if (candidate != null && _immediatePostDominator[block.Index] != candidate)
                    {
                        _immediatePostDominator[block.Index] = candidate;
                        changed = true;
                    }
                }
            }
        }

        /// <summary>두 노드가 같은 노드 위에 설 때까지 트리를 함께 거슬러 올린다.</summary>
        private BasicBlock Intersect(BasicBlock left, BasicBlock right)
        {
            var steps = 0;
            var bound = _graph.Blocks.Count * 2;

            while (left != right)
            {
                while (_reverseOrder[left.Index] > _reverseOrder[right.Index])
                {
                    left = _immediatePostDominator[left.Index];

                    if (left == null || steps++ > bound)
                    {
                        HitLimit = true;
                        return null;
                    }
                }

                while (_reverseOrder[right.Index] > _reverseOrder[left.Index])
                {
                    right = _immediatePostDominator[right.Index];

                    if (right == null || steps++ > bound)
                    {
                        HitLimit = true;
                        return null;
                    }
                }

                if (steps++ > bound)
                {
                    HitLimit = true;
                    return null;
                }
            }

            return left;
        }

        /// <summary>
        /// 각 결정의 나가는 엣지를 경로가 다시 합쳐지는 자리까지 걷는다.
        /// </summary>
        /// <remarks>
        /// 분기와 그 두 갈래가 다시 만나는 지점 사이의 모든 것이 그 분기가 돌리기로 결정한 코드다. 그 만나는
        /// 지점이 그것의 immediate post-dominator 이고, 그래서 그것을 먼저 계산해야 했다.
        /// </remarks>
        private void ComputeDependence()
        {
            var bound = _graph.Blocks.Count + 1;

            foreach (var decision in _graph.Blocks)
            {
                if (!decision.IsDecision || !_reachesExit[decision.Index])
                {
                    continue;
                }

                DecisionCount++;
                var rejoin = _immediatePostDominator[decision.Index];

                foreach (var successor in decision.Successors)
                {
                    if (!_reachesExit[successor.Index])
                    {
                        continue;
                    }

                    var runner = successor;
                    var steps = 0;

                    while (runner != null && runner != rejoin)
                    {
                        if (steps++ > bound)
                        {
                            HitLimit = true;
                            break;
                        }

                        Record(runner.Index, new Governor(decision.Index, successor.Index));
                        runner = _immediatePostDominator[runner.Index];
                    }
                }
            }
        }

        private void Record(int governed, Governor governor)
        {
            var governors = _dependsOn[governed];

            if (governors == null)
            {
                governors = new List<Governor>();
                _dependsOn[governed] = governors;
            }

            foreach (var existing in governors)
            {
                if (existing.Decision == governor.Decision && existing.Taken == governor.Taken)
                {
                    return;
                }
            }

            governors.Add(governor);
            DependenceCount++;
        }
    }
}
