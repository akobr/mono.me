namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Method)]
public sealed class StepAttribute : Attribute
{
    public Type? Lifetime { get; set; }
}
