namespace _42.nHolistic;

public static class LinkedListNodeExtensions
{
    public static bool IsBefore<T>(this LinkedListNode<T>? @this, LinkedListNode<T>? node)
    {
        var pointer = @this;

        if (ReferenceEquals(pointer, node) || node is null)
        {
            return false;
        }

        while (pointer?.Next is not null)
        {
            if (ReferenceEquals(pointer, node))
            {
                return true;
            }

            pointer = pointer.Next;
        }

        return false;
    }
}
