namespace _42.Structures.Graph
{
    public class DirectedGraph<TNodeEntity, TEdgeEntity> : GraphBase<TNodeEntity, TEdgeEntity>
    {
        protected override Edge<TNodeEntity, TEdgeEntity> AddEdge(
            Node<TNodeEntity, TEdgeEntity> fromNode,
            Node<TNodeEntity, TEdgeEntity> toNode,
            TEdgeEntity entity,
            uint weight)
        {
            if (fromNode.Edges.ContainsKey(toNode.Index))
            {
                throw new GraphException($"Edge between ({fromNode.Index};{toNode.Index}) already exists in the graph.");
            }

            var edge = new Edge<TNodeEntity, TEdgeEntity>(fromNode, toNode, entity) { Weight = weight };
            fromNode.Edges.Add(edge.To.Index, edge);
            return edge;
        }

        protected override bool RemoveEdge(Node<TNodeEntity, TEdgeEntity> fromNode, int toIndex)
        {
            return fromNode.Edges.Remove(toIndex);
        }
    }

    public class DirectedGraph<TNodeEntity> : DirectedGraph<TNodeEntity, object>
    {
        public IEdge<TNodeEntity, object> AddEdge(int fromIndex, int toIndex, uint weight = 1)
        {
            return AddEdge(fromIndex, toIndex, null!, weight);
        }
    }

    public class DirectedGraph : DirectedGraph<object>
    {
        public INode<object, object> AddNode()
        {
            return AddNode(null!);
        }

        public void AddNodeRange(int count)
        {
            AddNodeRange(new object[count]);
        }
    }
}
