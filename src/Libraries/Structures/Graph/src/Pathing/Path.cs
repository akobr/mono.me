using System.Collections.Generic;
using System.Text;

namespace _42.Structures.Graph.Pathing
{
    public class Path<TNodeEntity, TEdgeEntity> : IPath<TNodeEntity, TEdgeEntity>
    {
        private readonly IReadOnlyCollection<IEdge<TNodeEntity, TEdgeEntity>> _edges;

        public Path(
            INode<TNodeEntity, TEdgeEntity> fromNode,
            INode<TNodeEntity, TEdgeEntity> toNode,
            IReadOnlyCollection<IEdge<TNodeEntity, TEdgeEntity>> edges)
        {
            this._edges = edges;
            From = fromNode;
            To = toNode;
        }

        public INode<TNodeEntity, TEdgeEntity> From { get; }

        public INode<TNodeEntity, TEdgeEntity> To { get; }

        public int NodesCount => _edges.Count + 1;

        public int EdgesCount => _edges.Count;

        public IEnumerable<IEdge<TNodeEntity, TEdgeEntity>> GetEdges()
        {
            return _edges;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append($"{From.Index} -> {To.Index} (");

            foreach (var edge in _edges)
            {
                builder.Append(edge.From.Index);
                builder.Append(", ");
            }

            builder.Append(To.Index);
            builder.Append(")");
            return builder.ToString();
        }
    }
}
