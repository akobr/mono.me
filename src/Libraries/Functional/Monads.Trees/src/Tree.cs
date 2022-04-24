namespace _42.Functional.Monads.Trees
{
    public static class Tree
    {
        public static ITreeNode<TItem> Node<TItem>(TItem item, params TreeNode<TItem>[] children)
        {
            return new TreeNode<TItem>(item, children);
        }

        public static ITreeNode<TItem> Leaf<TItem>(TItem item)
        {
            return new TreeLeaf<TItem>(item);
        }
    }
}
