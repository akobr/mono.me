namespace _42.Structures.Graph.Walking
{
    public interface ISearchStrategy<TNodeEntity, TEdgeEntity>
    {
        bool CanMoveNext();

        INode<TNodeEntity, TEdgeEntity> GetNext();

        void Add(INode<TNodeEntity, TEdgeEntity> node);
    }
}
