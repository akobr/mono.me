using System;

namespace _42.Structures.Graph
{
    public interface IEdge : IEquatable<IEdge>
    {
        INode From { get; }

        INode To { get; }

        uint Weight { get; }
    }

    public interface IEdge<TNodeEntity, TEdgeEntity> : IEdge
    {
        TEdgeEntity Entity { get; }

        new INode<TNodeEntity, TEdgeEntity> From { get; }

        new INode<TNodeEntity, TEdgeEntity> To { get; }
    }
}
