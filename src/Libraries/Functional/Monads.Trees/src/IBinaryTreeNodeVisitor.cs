namespace _42.Functional.Monads.Trees
{
    public interface IBinaryTreeNodeVisitor<TItem, out TResult>
    {
        TResult Visit(BinaryTreeNode<TItem> node);

        TResult Visit(BinaryTreeLeaf<TItem> node);
    }
}