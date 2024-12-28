namespace _42.tHolistic;

public sealed class FromModelAttribute : InjectionAttribute
{
    public string? JQuery { get; set; }

    public override InjectionType InjectionType => InjectionType.Model;
}
