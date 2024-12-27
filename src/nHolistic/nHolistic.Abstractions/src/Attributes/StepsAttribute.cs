namespace _42.nHolistic;

// TODO: [P1] not sure about this one? how expensive it ti use only step on methods?
[AttributeUsage(AttributeTargets.Class)]
public sealed class StepsAttribute : Attribute
{
    public Type? StepLifetime { get; set; }
}
