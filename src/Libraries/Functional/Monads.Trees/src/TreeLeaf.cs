using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace _42.Functional.Monads.Trees
{
    public class TreeLeaf<TItem> : ITreeNode<TItem>
    {
        public TreeLeaf(TItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Item = item;
        }

        public TItem Item { get; }

        public ITreeNode<TResult> Select<TResult>(Func<TItem, TResult> selector)
        {
            return new TreeLeaf<TResult>(selector(Item));
        }

        IFunctor<TResult> IFunctor<TItem>.Select<TResult>(Func<TItem, TResult> selector)
        {
            return Select(selector);
        }

        public IEnumerator<ITreeNode<TItem>> GetEnumerator()
        {
            return Enumerable.Empty<ITreeNode<TItem>>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override bool Equals(object obj)
        {
            if (obj is TreeNode<TItem> other)
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
