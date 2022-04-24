using System;

namespace _42.Functional.Monads.Trees
{
    public static class BinaryTreeExtensions
    {
        public static IBinaryTreeNode<TResult> Select<TItem, TResult>(
            this IBinaryTreeNode<TItem> node,
            Func<TItem, TResult> selector)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            var visitor = new SelectBinaryTreeNodeVisitor<TItem, TResult>(selector);
            return node.Accept(visitor);
        }

#if MATH_NAMING
        public static IBinaryTreeNode<TResult> Map<TItem, TResult>(
            this IBinaryTreeNode<TItem> node,
            Func<TItem, TResult> selector)
        {
            return Select(node, selector);
        }
#endif
    }
}
