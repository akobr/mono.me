using System;

namespace _42.Functional.Monads.Trees
{
    public static class TreeExtensions
    {
#if MATH_NAMING
        public static ITreeNode<TResult> Map<TValue, TResult>(
            this ITreeNode<TValue> node,
            Func<TValue, TResult> selector)
        {
            return node.Select(selector);
        }
#endif
    }
}
