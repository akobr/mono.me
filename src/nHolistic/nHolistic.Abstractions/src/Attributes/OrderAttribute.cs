namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
