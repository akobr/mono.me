namespace _42.tHolistic;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public abstract class InjectionAttribute : Attribute
{
    public abstract InjectionType InjectionType { get; }
}
