using System.Collections.Generic;
using QuikGraph;

namespace _42.Monorepo.Repo.Generator
{
    internal static class GraphExtensions
    {
        public static IEnumerable<TVertex> GetAllAncestors<TVertex>(this IBidirectionalGraph<TVertex, IEdge<TVertex>> @this, TVertex vertex)
        {
            var ancestors = new HashSet<TVertex>();
            var visited = new HashSet<TVertex>();
            var toProcess = new Queue<TVertex>();
            toProcess.Enqueue(vertex);

            while (toProcess.Count > 0)
            {
                var vertexToProcess = toProcess.Dequeue();

                if (visited.Contains(vertexToProcess)
                    || !@this.TryGetInEdges(vertex, out var inEdges))
                {
                    continue;
                }

                visited.Add(vertexToProcess);

                foreach (var edge in inEdges)
                {
                    ancestors.Add(edge.Source);
                    toProcess.Enqueue(edge.Source);
                }
            }

            return ancestors;
        }
    }
}
