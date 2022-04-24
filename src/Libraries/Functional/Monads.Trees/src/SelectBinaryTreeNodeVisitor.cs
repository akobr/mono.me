using System;

namespace _42.Functional.Monads.Trees
{
    public class SelectBinaryTreeNodeVisitor<TItem, TResult> : IBinaryTreeNodeVisitor<TItem, IBinaryTreeNode<TResult>>
    {
        private readonly Func<TItem, TResult> _selector;

        public SelectBinaryTreeNodeVisitor(Func<TItem, TResult> selector)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        public IBinaryTreeNode<TResult> Visit(BinaryTreeLeaf<TItem> leaf)
        {
            var mappedItem = _selector(leaf.Item);
            return BinaryTree.Leaf(mappedItem);
        }

        public IBinaryTreeNode<TResult> Visit(BinaryTreeNode<TItem> node)
        {
            var mappedItem = _selector(node.Item);
            var mappedLeft = node.Left.Accept(this);
            var mappedRight = node.Right.Accept(this);
            return BinaryTree.Node(mappedItem, mappedLeft, mappedRight);
        }
    }
}
