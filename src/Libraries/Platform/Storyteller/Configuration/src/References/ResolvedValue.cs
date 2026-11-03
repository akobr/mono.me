namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// A resolved secret, with the time it was resolved; its value never reaches ToString().
internal sealed class ResolvedValue
{
    public ResolvedValue(string value, DateTimeOffset resolvedAt)
    {
        Value = value;
        ResolvedAt = resolvedAt;
    }

    public string Value { get; }

    public DateTimeOffset ResolvedAt { get; }

    public override string ToString()
    {
        return $"resolved at {ResolvedAt:O}";
    }
}
