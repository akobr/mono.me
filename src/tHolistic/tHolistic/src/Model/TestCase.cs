using Newtonsoft.Json.Linq;

namespace _42.tHolistic;

public class TestCase : IEquatable<TestCase>, IComparable<TestCase>, IComparable
{
    public required string FullyQualifiedName { get; init; }

    public required string DisplayName { get; init; }

    public required string Source { get; init; }

    public required string TypeFullyQualifiedName { get; init; }

    public string? MethodName { get; init; }

    public JToken? Model { get; init; }

    public HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TestCaseProperty> Traits { get; } = new();

    public List<TestCaseProperty> Properties { get; } = new();

    public bool Equals(TestCase? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((TestCase)obj);
    }

    public override int GetHashCode()
    {
        return FullyQualifiedName.GetHashCode(StringComparison.Ordinal);
    }

    public int CompareTo(TestCase? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1;
        }

        return string.Compare(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (ReferenceEquals(this, obj))
        {
            return 0;
        }

        return obj is TestCase other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(TestCase)}.", nameof(obj));
    }
}
