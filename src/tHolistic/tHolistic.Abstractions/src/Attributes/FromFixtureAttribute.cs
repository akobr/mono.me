namespace _42.tHolistic;

public sealed class FromFixtureAttribute : InjectionAttribute
{
    public string? Label { get; set; }


    public override InjectionType InjectionType => InjectionType.Fixture;
}
