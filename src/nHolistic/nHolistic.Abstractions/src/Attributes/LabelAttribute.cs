namespace _42.tHolistic;

[AttributeUsage(AttributeTargets.Class)]
public sealed class LabelAttribute(string label) : Attribute
{
    public string Label { get; set; } = label;
}
