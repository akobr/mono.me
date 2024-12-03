namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public abstract class InjectionAttribute : Attribute
{
    public abstract InjectionType InjectionType { get; }
}
