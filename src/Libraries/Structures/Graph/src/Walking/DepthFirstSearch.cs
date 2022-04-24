using System.Collections.Generic;

namespace _42.Structures.Graph.Walking
{
    public class DepthFirstSearch<TNodeEntity, TEdgeEntity> : ISearchStrategy<TNodeEntity, TEdgeEntity>
    {
        private readonly Stack<INode<TNodeEntity, TEdgeEntity>> _stack = new Stack<INode<TNodeEntity, TEdgeEntity>>();

        public void Add(INode<TNodeEntity, TEdgeEntity> node)
        {
            _stack.Push(node);
        }

        public INode<TNodeEntity, TEdgeEntity> GetNext()
        {
            return _stack.Pop();
        }

        public bool CanMoveNext()
        {
            return _stack.Count > 0;
        }
    }

    public class DepthFirstSearch<TNodeEntity> : DepthFirstSearch<TNodeEntity, object>
    {
        // no member ( template type )
    }

    public class DepthFirstSearch : DepthFirstSearch<object, object>
    {
        // no member ( template type )
    }
}
