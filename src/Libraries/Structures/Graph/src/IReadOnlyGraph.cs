using System.Collections.Generic;

namespace _42.Structures.Graph
{
    public interface IReadOnlyGraph<TNodeEntity, TEdgeEntity>
    {
        int NodeCount { get; }

        int EdgeCount { get; }

        INode<TNodeEntity, TEdgeEntity> GetNode(int index);

        IEnumerable<INode<TNodeEntity, TEdgeEntity>> GetNodes();

        bool ContainsEdge(int fromIndex, int toIndex);

        IEdge<TNodeEntity, TEdgeEntity> GetEdge(int fromIndex, int toIndex);

        bool TryGetEdge(int fromIndex, int toIndex, out IEdge<TNodeEntity, TEdgeEntity> edge);
    }
}
