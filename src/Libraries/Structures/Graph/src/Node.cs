using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace _42.Structures.Graph
{
    public class Node<TNodeEntity, TEdgeEntity> : INode<TNodeEntity, TEdgeEntity>
    {
        private const int DEFAULT_EDGE_LIST_CAPACITY = 4;
        private readonly Dictionary<int, Edge<TNodeEntity, TEdgeEntity>> _edges;

        public Node(int id, TNodeEntity entity)
        {
            Index = id;
            Entity = entity;
            _edges = new Dictionary<int, Edge<TNodeEntity, TEdgeEntity>>(DEFAULT_EDGE_LIST_CAPACITY);
        }

        public int Index { get; }

        public TNodeEntity Entity { get; }

        public Dictionary<int, Edge<TNodeEntity, TEdgeEntity>> Edges => _edges;

        public int GetEdgeCount()
        {
            return _edges.Count;
        }

        public IEnumerable<INode<TNodeEntity, TEdgeEntity>> GetSuccessors()
        {
            return _edges.Values.Select(edge => edge.To);
        }

        public IEnumerable<IEdge<TNodeEntity, TEdgeEntity>> GetEdges()
        {
            return _edges.Values;
        }

        public IEdge<TNodeEntity, TEdgeEntity> GetEdge(int to)
        {
            if (!_edges.TryGetValue(to, out var typedEdge))
            {
                throw new ArgumentOutOfRangeException(nameof(to), $"No edge to [{to}] node.");
            }

            return typedEdge;
        }

        public bool Equals(int other)
        {
            return Index.Equals(other);
        }

        public bool Equals(INode? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Equals(other.Index);
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

            if (other is INode otherNode)
            {
                return Equals(otherNode);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Index;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append($"{Index} -> ");

            if (_edges.Count < 1)
            {
                builder.Append("null");
                return builder.ToString();
            }

            foreach (var successorIndex in _edges.Keys.OrderBy(edge => edge))
            {
                builder.Append(successorIndex);
                builder.Append(", ");
            }

            builder.Remove(builder.Length - 2, 2);
            return builder.ToString();
        }
    }
}
