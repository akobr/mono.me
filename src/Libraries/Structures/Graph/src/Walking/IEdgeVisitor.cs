namespace _42.Structures.Graph.Walking
{
    public interface IEdgeVisitor<TNodeEntity, TEdgeEntity>
    {
        void Visit(IEdge<TNodeEntity, TEdgeEntity> edge);
    }
}
