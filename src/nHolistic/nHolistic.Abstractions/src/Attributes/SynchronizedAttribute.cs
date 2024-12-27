namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SynchronizedAttribute(string synchronizationKey) : Attribute
{
    public string SynchronizationKey { get; } = synchronizationKey;
}
