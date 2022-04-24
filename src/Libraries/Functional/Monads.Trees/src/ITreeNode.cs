using System;
using System.Collections.Generic;

namespace _42.Functional.Monads.Trees
{
    public interface ITreeNode<out TItem> : IFunctor<TItem>, IEnumerable<ITreeNode<TItem>>
    {
        TItem Item { get; }

        new ITreeNode<TResult> Select<TResult>(Func<TItem, TResult> selector);
    }
}