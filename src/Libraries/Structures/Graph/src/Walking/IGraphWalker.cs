namespace _42.Structures.Graph.Walking
{
    public interface IGraphWalker<TNodeEntity, TEdgeEntity>
    {
        void Walk(IGraph<TNodeEntity, TEdgeEntity> graph, params int[] startIndexes);
    }
}
