using System.Collections.Generic;

namespace _42.Structures.Graph
{
    public interface IGraph<TNodeEntity, TEdgeEntity> : IReadOnlyGraph<TNodeEntity, TEdgeEntity>
    {
        INode<TNodeEntity, TEdgeEntity> AddNode(TNodeEntity entity);

        IReadOnlyList<INode<TNodeEntity, TEdgeEntity>> AddNodeRange(IEnumerable<TNodeEntity> entities);

        IEdge<TNodeEntity, TEdgeEntity> AddEdge(int fromIndex, int toIndex, TEdgeEntity entity, uint weight = 1);

        bool RemoveEdge(int fromIndex, int toIndex);
    }
}
