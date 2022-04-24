using System;
using System.Collections.Generic;

namespace _42.Structures.Graph
{
    public interface INode : IEquatable<INode>, IEquatable<int>
    {
        int Index { get; }

        int GetEdgeCount();
    }

    public interface INode<TNodeEntity, TEdgeEntity> : INode
    {
        TNodeEntity Entity { get; }

        IEnumerable<IEdge<TNodeEntity, TEdgeEntity>> GetEdges();

        IEnumerable<INode<TNodeEntity, TEdgeEntity>> GetSuccessors();

        IEdge<TNodeEntity, TEdgeEntity> GetEdge(int to);
    }
}
