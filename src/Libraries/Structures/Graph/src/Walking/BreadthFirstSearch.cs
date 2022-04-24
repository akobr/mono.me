using System.Collections.Generic;

namespace _42.Structures.Graph.Walking
{
    public class BreadthFirstSearch<TNodeEntity, TEdgeEntity> : ISearchStrategy<TNodeEntity, TEdgeEntity>
    {
        private readonly Queue<INode<TNodeEntity, TEdgeEntity>> _queue = new Queue<INode<TNodeEntity, TEdgeEntity>>();

        public void Add(INode<TNodeEntity, TEdgeEntity> node)
        {
            _queue.Enqueue(node);
        }

        public INode<TNodeEntity, TEdgeEntity> GetNext()
        {
            return _queue.Dequeue();
        }

        public bool CanMoveNext()
        {
            return _queue.Count > 0;
        }
    }

    public class BreadthFirstSearch<TNodeEntity> : BreadthFirstSearch<TNodeEntity, object>
    {
        // no member ( template type )
    }

    public class BreadthFirstSearch : BreadthFirstSearch<object, object>
    {
        // no member ( template type )
    }
}
