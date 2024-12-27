namespace _42.nHolistic;

// TODO: [P1] move out of core
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
