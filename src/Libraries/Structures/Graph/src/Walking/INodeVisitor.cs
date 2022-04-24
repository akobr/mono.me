namespace _42.Structures.Graph.Walking
{
    public interface INodeVisitor<TNodeEntity, TEdgeEntity>
    {
        void Visit(INode<TNodeEntity, TEdgeEntity> node);
    }
}
