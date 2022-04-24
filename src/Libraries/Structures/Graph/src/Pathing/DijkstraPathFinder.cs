using System;
using System.Collections.Generic;

namespace _42.Structures.Graph.Pathing
{
    public class DijkstraPathFinder<TNodeEntity, TEdgeEntity> : IPathFinder<TNodeEntity, TEdgeEntity>
    {
        private readonly IGraph<TNodeEntity, TEdgeEntity> _graph;
        private readonly Dictionary<int, Path<TNodeEntity, TEdgeEntity>[]> _cache;

        public DijkstraPathFinder(IGraph<TNodeEntity, TEdgeEntity> graph)
        {
            _graph = graph;
            _cache = new Dictionary<int, Path<TNodeEntity, TEdgeEntity>[]>();
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public IPath<TNodeEntity, TEdgeEntity>? GetShortestPath(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _graph.NodeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex), "Index of requested start node is not in the graph.");
            }

            if (toIndex < 0 || toIndex >= _graph.NodeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex), "Index of requested end node is not in the graph.");
            }

            if (TryRetrieveFromCache(fromIndex, toIndex, out var path))
            {
                return path;
            }

            CalculateAndStoreShortestPathsFrom(fromIndex);
            return _cache[fromIndex][toIndex];
        }

        private bool TryRetrieveFromCache(int fromIndex, int toIndex, out IPath<TNodeEntity, TEdgeEntity>? path)
        {
            if (!_cache.TryGetValue(fromIndex, out var paths))
            {
                path = null;
                return false;
            }

            path = paths[toIndex];
            return true;
        }

        private void CalculateAndStoreShortestPathsFrom(int fromIndex)
        {
            var predecessors = CalculateShortestPathsFrom(fromIndex);
            var paths = new Path<TNodeEntity, TEdgeEntity>[_graph.NodeCount];

            for (int i = 0; i < _graph.NodeCount; i++)
            {
                if (predecessors[i] == null)
                {
                    continue;
                }

                paths[i] = BuildPath(fromIndex, i, predecessors);
            }

            _cache[fromIndex] = paths;
        }

        private Path<TNodeEntity, TEdgeEntity> BuildPath(int fromIndex, int toIndex, INode<TNodeEntity, TEdgeEntity>?[] predecessors)
        {
            var edges = new Stack<IEdge<TNodeEntity, TEdgeEntity>>();
            var parent = predecessors[toIndex];
            int nodeIndex = toIndex;

            while (parent != null)
            {
                edges.Push(parent.GetEdge(nodeIndex));
                nodeIndex = parent.Index;
                parent = predecessors[parent.Index];
            }

            return new Path<TNodeEntity, TEdgeEntity>(_graph.GetNode(fromIndex), _graph.GetNode(toIndex), edges);
        }

        private INode<TNodeEntity, TEdgeEntity>?[] CalculateShortestPathsFrom(int fromIndex)
        {
            var fromNode = _graph.GetNode(fromIndex);
            var weights = new uint[_graph.NodeCount];
            var predecessors = new INode<TNodeEntity, TEdgeEntity>?[_graph.NodeCount];
            var priorityQueue = new PriorityQueue<INode<TNodeEntity, TEdgeEntity>, double>();

            SetMaxWeights(weights);
            priorityQueue.Enqueue(fromNode, 0);
            weights[fromIndex] = 0;

            while (priorityQueue.Count > 0)
            {
                var node = priorityQueue.Dequeue();

                foreach (var edge in node.GetEdges())
                {
                    var successor = edge.To;
                    int successorIndex = successor.Index;
                    uint newWeight = weights[node.Index] + edge.Weight;

                    if (newWeight >= weights[successorIndex])
                    {
                        continue;
                    }

                    weights[successorIndex] = newWeight;
                    predecessors[successorIndex] = node;
                    priorityQueue.Enqueue(successor, newWeight);
                }
            }

            return predecessors;
        }

        private void SetMaxWeights(uint[] weights)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = uint.MaxValue;
            }
        }
    }

    public class DijkstraPathFinder<TNodeEntity> : DijkstraPathFinder<TNodeEntity, object>
    {
        public DijkstraPathFinder(IGraph<TNodeEntity, object> graph)
            : base(graph)
        {
            // no operation ( template type )
        }
    }

    public class DijkstraPathFinder : DijkstraPathFinder<object, object>
    {
        public DijkstraPathFinder(IGraph<object, object> graph)
            : base(graph)
        {
            // no operation ( template type )
        }
    }

}
