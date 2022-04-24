using System.Linq;

namespace _42.Structures.Graph.Walking
{
    public class SearchWalker<TSearchStrategy, TNodeEntity, TEdgeEntity> : IGraphWalker<TNodeEntity, TEdgeEntity>
        where TSearchStrategy : ISearchStrategy<TNodeEntity, TEdgeEntity>, new()
    {
        private readonly INodeVisitor<TNodeEntity, TEdgeEntity>? _nodeVisitor;
        private readonly IEdgeVisitor<TNodeEntity, TEdgeEntity>? _edgeVisitor;

        public SearchWalker(
            INodeVisitor<TNodeEntity, TEdgeEntity>? nodeVisitor,
            IEdgeVisitor<TNodeEntity, TEdgeEntity>? edgeVisitor)
        {
            _nodeVisitor = nodeVisitor;
            _edgeVisitor = edgeVisitor;
        }

        public void Walk(IGraph<TNodeEntity, TEdgeEntity> graph, params int[] startIndexes)
        {
            var predecessors = new INode<TNodeEntity, TEdgeEntity>?[graph.NodeCount];
            var states = new NodeState[graph.NodeCount];
            var strategy = new TSearchStrategy();

            foreach (int startIndex in startIndexes.Distinct())
            {
                var startNode = graph.GetNode(startIndex);
                predecessors[startIndex] = null;
                states[startIndex] = NodeState.Open;
                strategy.Add(startNode);
            }

            while (strategy.CanMoveNext())
            {
                var node = strategy.GetNext();
                var predecessor = predecessors[node.Index];

                if (_edgeVisitor != null
                    && predecessor != null)
                {
                    _edgeVisitor.Visit(predecessor.GetEdge(node.Index));
                }

                _nodeVisitor?.Visit(node);

                foreach (var successor in node.GetSuccessors())
                {
                    if (states[successor.Index] == NodeState.Fresh)
                    {
                        predecessors[successor.Index] = node;
                        states[successor.Index] = NodeState.Open;
                        strategy.Add(successor);
                    }
                }

                states[node.Index] = NodeState.Closed;
            }
        }
    }

    public class SearchWalker<TSearchStrategy, TNodeEntity> : SearchWalker<TSearchStrategy, TNodeEntity, object>
        where TSearchStrategy : ISearchStrategy<TNodeEntity, object>, new()
    {
        public SearchWalker(
            INodeVisitor<TNodeEntity, object> nodeVisitor,
            IEdgeVisitor<TNodeEntity, object> edgeVisitor)
            : base(nodeVisitor, edgeVisitor)
        {
            // no operation ( template type )
        }
    }

    public class SearchWalker<TSearchStrategy> : SearchWalker<TSearchStrategy, object, object>
        where TSearchStrategy : ISearchStrategy<object, object>, new()
    {
        public SearchWalker(
            INodeVisitor<object, object> nodeVisitor,
            IEdgeVisitor<object, object> edgeVisitor)
            : base(nodeVisitor, edgeVisitor)
        {
            // no operation ( template type )
        }
    }
}
