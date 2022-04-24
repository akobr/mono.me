using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace _42.Structures.Graph
{
    public abstract class GraphBase<TNodeEntity, TEdgeEntity> : IGraph<TNodeEntity, TEdgeEntity>
    {
        private int _edgesCount;
        protected readonly List<Node<TNodeEntity, TEdgeEntity>> _nodes;

        protected GraphBase()
        {
            _nodes = new List<Node<TNodeEntity, TEdgeEntity>>();
        }

        public int NodeCount => _nodes.Count;

        public int EdgeCount => _edgesCount;

        public IEdge<TNodeEntity, TEdgeEntity> AddEdge(int fromIndex, int toIndex, TEdgeEntity entity, uint weight = 1)
        {
            var fromNode = _nodes[fromIndex];
            var toNode = _nodes[toIndex];

            var edge = AddEdge(fromNode, toNode, entity, weight);
            _edgesCount++;
            return edge;
        }

        public INode<TNodeEntity, TEdgeEntity> AddNode(TNodeEntity entity)
        {
            var node = new Node<TNodeEntity, TEdgeEntity>(_nodes.Count, entity);
            _nodes.Add(node);
            return node;
        }

        public IReadOnlyList<INode<TNodeEntity, TEdgeEntity>> AddNodeRange(IEnumerable<TNodeEntity> entities)
        {
            return entities.Select(AddNode).ToList();
        }

        public bool ContainsEdge(int fromIndex, int toIndex)
        {
            return fromIndex < 0
                || fromIndex >= _nodes.Count
                || !_nodes[fromIndex].Edges.ContainsKey(toIndex);
        }

        public IEdge<TNodeEntity, TEdgeEntity> GetEdge(int fromIndex, int toIndex)
        {
            var fromNode = _nodes[fromIndex];

            if (!fromNode.Edges.TryGetValue(toIndex, out var typedEdge))
            {
                throw new GraphException($"Edge between [{fromIndex};{toIndex}] doesn't exist in the graph.");
            }

            return typedEdge;
        }

        public bool TryGetEdge(int fromIndex, int toIndex, out IEdge<TNodeEntity, TEdgeEntity> edge)
        {
            if (fromIndex < 0
                || fromIndex >= _nodes.Count
                || !_nodes[fromIndex].Edges.TryGetValue(toIndex, out var typedEdge))
            {
                edge = default!;
                return false;
            }

            edge = typedEdge;
            return true;
        }

        public INode<TNodeEntity, TEdgeEntity> GetNode(int index)
        {
            return _nodes[index];
        }

        public IEnumerable<INode<TNodeEntity, TEdgeEntity>> GetNodes()
        {
            return _nodes;
        }

        public bool RemoveEdge(int fromIndex, int toIndex)
        {
            return fromIndex >= 0
                && fromIndex < _nodes.Count
                && RemoveEdge(_nodes[fromIndex], toIndex);
        }

        protected abstract Edge<TNodeEntity, TEdgeEntity> AddEdge(
            Node<TNodeEntity, TEdgeEntity> fromNode,
            Node<TNodeEntity, TEdgeEntity> toNode,
            TEdgeEntity entity,
            uint weight);

        protected abstract bool RemoveEdge(Node<TNodeEntity, TEdgeEntity> fromNode, int toIndex);

        protected void CheckNodeIndex(int index)
        {
            if (index < 0 && index >= _nodes.Count)
            {
                throw new IndexOutOfRangeException();
            }
        }

        /// <summary>
        /// Returns string representation of the graph as an adjacency matrix.
        /// </summary>
        public override string ToString()
        {
            if (_nodes.Count < 1)
            {
                return "| null |";
            }

            StringBuilder builder = new StringBuilder();

            foreach (var node in _nodes)
            {
                builder.Append("|");

                for (int s = 0; s < _nodes.Count; s++)
                {
                    builder.Append(node.Edges.ContainsKey(s) ? " 1" : " 0");
                }

                builder.Append(" |\n");
            }

            builder.Remove(builder.Length - 1, 1);
            return builder.ToString();
        }
    }
}
