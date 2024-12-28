namespace _42.tHolistic;

[AttributeUsage(AttributeTargets.Method)]
public sealed class StepAttribute : Attribute
{
    public Type? Lifetime { get; set; }
}
