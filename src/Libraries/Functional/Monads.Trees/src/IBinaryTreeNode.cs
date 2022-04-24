namespace _42.Functional.Monads.Trees
{
    public interface IBinaryTreeNode<TItem>
    {
        TResult Accept<TResult>(IBinaryTreeNodeVisitor<TItem, TResult> visitor);
    }
}