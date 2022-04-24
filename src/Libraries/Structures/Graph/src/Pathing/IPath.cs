using System.Collections.Generic;

namespace _42.Structures.Graph.Pathing
{
    public interface IPath<TNodeEntity, TEdgeEntity>
    {
        INode<TNodeEntity, TEdgeEntity> From { get; }

        INode<TNodeEntity, TEdgeEntity> To { get; }

        int NodesCount { get; }

        int EdgesCount { get; }

        IEnumerable<IEdge<TNodeEntity, TEdgeEntity>> GetEdges();
    }
}
