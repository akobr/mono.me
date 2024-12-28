using QuikGraph;

namespace _42.tHolistic;

public static class AdjacencyGraphExtensions
{
    public static List<List<T>> FindCycles<T>(this AdjacencyGraph<T, Edge<T>> graph)
    {
        var cycles = new List<List<T>>();
        var stack = new Stack<T>();
        var visited = new HashSet<T>();
        var recursionStack = new HashSet<T>();

        void DepthFirstSearch(T vertex)
        {
            visited.Add(vertex);
            recursionStack.Add(vertex);
            stack.Push(vertex);

            foreach (var edge in graph.OutEdges(vertex))
            {
                if (!visited.Contains(edge.Target))
                {
                    DepthFirstSearch(edge.Target);
                }
                else if (recursionStack.Contains(edge.Target))
                {
                    var cycle = new List<T>();
                    foreach (var v in stack)
                    {
                        cycle.Add(v);

                        if (v.Equals(edge.Target))
                        {
                            break;
                        }
                    }

                    cycle.Reverse();
                    cycles.Add(cycle);
                }
            }

            stack.Pop();
            recursionStack.Remove(vertex);
        }

        foreach (var vertex in graph.Vertices)
        {
            if (!visited.Contains(vertex))
            {
                DepthFirstSearch(vertex);
            }
        }

        return cycles;
    }
}
