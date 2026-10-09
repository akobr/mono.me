namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Limits of one resolved read that contains object bindings.
/// Steps are the deterministic, user-facing limit. Time and allocation are backstops for work inside one primitive.
/// </summary>
public sealed record ObjectBindingLimits
{
    public static ObjectBindingLimits Default { get; } = new();

    /// <summary>
    /// Gets interpreter steps in one read, shared by every envelope of the read.
    /// </summary>
    public long MaxSteps { get; init; } = 1_000_000;

    /// <summary>
    /// Gets bytes allocated on the evaluating thread inside the engines in one read.
    /// </summary>
    public long MaxAllocatedBytes { get; init; } = 64L << 20;

    /// <summary>
    /// Gets time spent inside the engines in one read. Asynchronous <c>@</c> resolution between envelopes does not count.
    /// </summary>
    public TimeSpan MaxEvaluationTime { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets nested envelope evaluations in one read.
    /// </summary>
    public int MaxEnvelopeDepth { get; init; } = 32;

    /// <summary>
    /// Gets envelope evaluations in one read.
    /// </summary>
    public int MaxEnvelopeEvaluations { get; init; } = 256;

    /// <summary>
    /// Gets recursion depth of the interpreter, the expression parser, and expression evaluation inside one engine call.
    /// </summary>
    public int MaxDepth { get; init; } = 512;

    /// <summary>
    /// Gets serialized characters of one envelope result.
    /// </summary>
    public int MaxResultLength { get; init; } = 100_000;

    /// <summary>
    /// Gets nesting of arrays and objects in one envelope result. Matches the reader depth used to convert results.
    /// </summary>
    public int MaxResultDepth { get; init; } = 64;

    internal void Validate()
    {
        Positive(MaxSteps, nameof(MaxSteps));
        Positive(MaxAllocatedBytes, nameof(MaxAllocatedBytes));
        Positive(MaxEvaluationTime.Ticks, nameof(MaxEvaluationTime));
        Positive(MaxEnvelopeDepth, nameof(MaxEnvelopeDepth));
        Positive(MaxEnvelopeEvaluations, nameof(MaxEnvelopeEvaluations));
        Positive(MaxDepth, nameof(MaxDepth));
        Positive(MaxResultLength, nameof(MaxResultLength));
        Positive(MaxResultDepth, nameof(MaxResultDepth));
    }

    private static void Positive(long value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{nameof(ObjectBindingLimits)}.{name} must be positive.");
        }
    }
}
