namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// An object-binding read ran into one of its evaluation limits.
/// </summary>
public sealed class EvaluationLimitExceededException : BindingEvaluationException
{
    public EvaluationLimitExceededException(EvaluationLimitKind kind, long limit, string message)
        : base(message)
    {
        Kind = kind;
        Limit = limit;
    }

    public EvaluationLimitExceededException(
        EvaluationLimitKind kind,
        long limit,
        string message,
        string? path,
        Exception? innerException)
        : base(message, path, innerException)
    {
        Kind = kind;
        Limit = limit;
    }

    public EvaluationLimitKind Kind { get; }

    /// <summary>
    /// Gets the configured value of the limit (steps, milliseconds, bytes, depth, characters, or envelopes).
    /// </summary>
    public long Limit { get; }
}
