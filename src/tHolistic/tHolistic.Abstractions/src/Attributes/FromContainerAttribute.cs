namespace _42.tHolistic;

public sealed class FromContainerAttribute : InjectionAttribute
{
    public string? ServiceName { get; set; }

    public Type? ServiceType { get; set; }

    public override InjectionType InjectionType => InjectionType.Container;
}
