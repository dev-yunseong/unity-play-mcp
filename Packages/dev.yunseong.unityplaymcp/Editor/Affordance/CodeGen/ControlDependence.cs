using System.Collections.Generic;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>결정 하나와, 거기 닿기 위해 그 결정의 어느 `branch` 를 탔는지.</summary>
    /// <remarks>
    /// 같은 비교가 한 엣지에서는 <c>StagePosition &gt;= 1</c>, 다른 엣지에서는 <c>&lt; 1</c> 이므로 어느 `branch` 인지가 필요하다.
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
    /// 각 블록이 어떤 결정에 control dependent 한지.
    /// </summary>
    /// <remarks>
    /// 키가 지키는 코드는 그 키의 분기에 control dependent 한 블록이고, 선행 조건은 블록이 control dependent 한
    /// 결정이다. 이렇게 보면 <c>A || B</c> 의 단락 평가도 엣지 하나일 뿐이라 따로 다룰 필요가 없다.
    ///
    /// control dependence 는 post-dominance 로 정의되므로 그것을 먼저 계산한다.
    /// </remarks>
    internal sealed class ControlDependence
    {
        /// <summary>
        /// 고정점 루프의 최대 반복 횟수. 넘으면 그래프를 포기한다.
        /// </summary>
        /// <remarks>
        /// 정상 입력은 몇 번 만에 수렴하므로 수렴하지 않는 입력만 걸린다.
        /// </remarks>
        private const int MaxPasses = 200;

        private readonly ControlFlowGraph _graph;
        private readonly BasicBlock[] _immediatePostDominator;
        private readonly int[] _reverseOrder;
        private readonly bool[] _reachesExit;
        private readonly List<Governor>[] _dependsOn;

        /// <summary>exit 에 닿는 경로가 없어 빼 둔 블록 수.</summary>
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
        /// post-dominance 는 exit 로 가는 경로가 있는 블록에만 정의된다. 끝나지 않는 루프 같은 블록은 immediate
        /// post-dominator 가 없어 비교하면 무한히 걷게 되고 에디터가 멈춘다. 그래서 그런 블록은 추측하지 않고 세어서 뺀다.
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

            // 명시적 스택으로 걷는다. 재귀는 깊은 메서드에서 스택을 넘치게 해 에디터를 죽인다.
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

        /// <summary>두 노드가 같은 노드에 닿을 때까지 트리를 함께 거슬러 오른다.</summary>
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
        /// 각 결정의 나가는 엣지를 경로가 다시 합쳐지는 곳까지 걷는다.
        /// </summary>
        /// <remarks>
        /// 분기와 그 immediate post-dominator 사이의 모든 블록이 그 분기에 control dependent 하다.
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
