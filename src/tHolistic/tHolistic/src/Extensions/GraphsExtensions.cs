using QuikGraph;

namespace _42.tHolistic;

public static class GraphsExtensions
{
    public static TVertex? GetRootVertex<TVertex>(this BidirectionalGraph<TVertex, Edge<TVertex>> @this)
    {
        var vertex = @this.Vertices.FirstOrDefault();

        if (vertex is null)
        {
            return default;
        }

        while (@this.TryGetInEdges(vertex, out var edges))
        {
            var edge = edges.FirstOrDefault();

            if (edge is null)
            {
                break;
            }

            vertex = edge.Source;
        }

        return vertex;
    }
}
