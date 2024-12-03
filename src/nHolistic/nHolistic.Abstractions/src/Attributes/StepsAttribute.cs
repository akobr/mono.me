namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Class)]
public sealed class StepsAttribute : Attribute
{
    public Type? StepLifetime { get; set; }
}
