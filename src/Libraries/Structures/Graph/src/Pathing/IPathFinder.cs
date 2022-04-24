namespace _42.Structures.Graph.Pathing
{
    public interface IPathFinder<TNodeEntity, TEdgeEntity>
    {
        IPath<TNodeEntity, TEdgeEntity>? GetShortestPath(int fromIndex, int toIndex);
    }
}
