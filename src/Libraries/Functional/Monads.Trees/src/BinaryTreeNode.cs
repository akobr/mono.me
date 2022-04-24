using System;

namespace _42.Functional.Monads.Trees
{
    public class BinaryTreeNode<TItem> : IBinaryTreeNode<TItem>
    {
        public BinaryTreeNode(TItem item, IBinaryTreeNode<TItem> left, IBinaryTreeNode<TItem> right)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Item = item;
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public TItem Item { get; }

        public IBinaryTreeNode<TItem> Left { get; }

        public IBinaryTreeNode<TItem> Right { get; }

        public TResult Accept<TResult>(IBinaryTreeNodeVisitor<TItem, TResult> visitor)
        {
            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            return visitor.Visit(this);
        }

        public override bool Equals(object obj)
        {
            if (obj is BinaryTreeNode<TItem> other)
            {
                return Equals(Item, other.Item)
                       && Equals(Left, other.Left)
                       && Equals(Right, other.Right);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Item.GetHashCode() ^ Left.GetHashCode() ^ Right.GetHashCode();
        }
    }
}
