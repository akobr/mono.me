using System;

namespace _42.Functional.Monads.Trees
{
    public class BinaryTreeLeaf<T> : IBinaryTreeNode<T>
    {
        public BinaryTreeLeaf(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Item = item;
        }

        public T Item { get; }

        public TResult Accept<TResult>(IBinaryTreeNodeVisitor<T, TResult> visitor)
        {
            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            return visitor.Visit(this);
        }

        public override bool Equals(object obj)
        {
            if (obj is TreeLeaf<T> other)
            {
                return Equals(Item, other.Item);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Item.GetHashCode();
        }
    }
}
