namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Class)]
public sealed class LabelAttribute(string label) : Attribute
{
    public string Label { get; set; } = label;
}
