using System.Reflection;

namespace _42.nHolistic;

public class TestCaseContext : IEquatable<TestCaseContext>, IComparable<TestCaseContext>, IComparable
{
    public required TestCase Case { get; init; }

    public required Assembly Assembly { get; init; }

    public required Type TargetType { get; init; }

    public MethodInfo? TargetMethod { get; init; }

    public required TestAttribute Attribute { get; init; }

    public Guid? BatchId { get; set; }

    public int? StageIndex { get; set; }

    public TestCaseExecutionState State { get; set; }

    public bool Equals(TestCaseContext? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Case.Equals(other.Case);
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

        return Equals((TestCaseContext)obj);
    }

    public override int GetHashCode()
    {
        return Case.GetHashCode();
    }

    public int CompareTo(TestCaseContext? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1;
        }

        return Case.CompareTo(other.Case);
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

        return obj is TestCaseContext other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(TestCaseContext)}.", nameof(obj));
    }
}
