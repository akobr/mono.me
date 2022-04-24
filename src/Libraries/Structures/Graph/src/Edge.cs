namespace _42.Structures.Graph
{
    public class Edge<TNodeEntity, TEdgeEntity> : IEdge<TNodeEntity, TEdgeEntity>
    {
        private readonly Node<TNodeEntity, TEdgeEntity> _from;
        private readonly Node<TNodeEntity, TEdgeEntity> _to;

        public Edge(Node<TNodeEntity, TEdgeEntity> from, Node<TNodeEntity, TEdgeEntity> to, TEdgeEntity entity)
        {
            _from = from;
            _to = to;
            Entity = entity;
            Weight = 1;
        }

        public TEdgeEntity Entity { get; }

        public INode<TNodeEntity, TEdgeEntity> From => _from;

        public INode<TNodeEntity, TEdgeEntity> To => _to;

        public uint Weight { get; set; }

        INode IEdge.From => _from;

        INode IEdge.To => _to;

        public bool Equals(IEdge? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return From.Equals(other.From) && To.Equals(other.To);
        }

        public override bool Equals(object? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is IEdge edge)
            {
                return Equals(edge);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _from.GetHashCode() ^ _to.GetHashCode();
        }

        public override string ToString()
        {
            return Weight <= 1
                ? $"({_from.Index};{_to.Index})"
                : $"({_from.Index};{_to.Index})[{Weight}]";
        }
    }
}
