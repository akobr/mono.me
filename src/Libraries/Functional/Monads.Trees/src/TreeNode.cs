using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace _42.Functional.Monads.Trees
{
    public class TreeNode<TItem> : ITreeNode<TItem>
    {
        private readonly IEnumerable<ITreeNode<TItem>> _children;

        public TreeNode(TItem item, IEnumerable<ITreeNode<TItem>> children)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Item = item;
            _children = children ?? throw new ArgumentNullException(nameof(children));
        }

        public TItem Item { get; }

        public ITreeNode<TResult> Select<TResult>(Func<TItem, TResult> selector)
        {
            return new TreeNode<TResult>(
                selector(Item),
                _children.Select(child => child.Select(selector)));
        }

        IFunctor<TResult> IFunctor<TItem>.Select<TResult>(Func<TItem, TResult> selector)
        {
            return Select(selector);
        }

        public IEnumerator<ITreeNode<TItem>> GetEnumerator()
        {
            return _children.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _children.GetEnumerator();
        }

        public override bool Equals(object obj)
        {
            if (obj is TreeNode<TItem> other)
            {
                return Equals(Item, other.Item)
                       && this.SequenceEqual(other);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Item.GetHashCode() ^ _children.GetHashCode();
        }
    }
}
