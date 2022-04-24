namespace _42.Functional.Monads.Trees
{
    public static class BinaryTree
    {
        public static IBinaryTreeNode<TItem> Leaf<TItem>(TItem item)
        {
            return new BinaryTreeLeaf<TItem>(item);
        }

        public static IBinaryTreeNode<TItem> Node<TItem>(
            TItem item,
            IBinaryTreeNode<TItem> left,
            IBinaryTreeNode<TItem> right)
        {
            return new BinaryTreeNode<TItem>(item, left, right);
        }
    }
}
