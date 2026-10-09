namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// The limit an object-binding read ran into.
/// </summary>
public enum EvaluationLimitKind
{
    /// <summary>Interpreter steps in one read. Deterministic.</summary>
    Steps,

    /// <summary>Time spent inside the engines in one read.</summary>
    Time,

    /// <summary>Bytes allocated inside the engines in one read.</summary>
    Memory,

    /// <summary>Recursion depth of the interpreter, the expression parser, or the stack.</summary>
    Depth,

    /// <summary>Serialized length or nesting of one envelope's result.</summary>
    Result,

    /// <summary>Nested envelope evaluations in one read.</summary>
    EnvelopeDepth,

    /// <summary>Envelope evaluations in one read.</summary>
    EnvelopeCount,
}
