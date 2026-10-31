namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// The limit a binding read ran into.
/// </summary>
public enum EvaluationLimitKind
{
    /// <summary>Interpreter steps in one read. Deterministic.</summary>
    Steps,

    /// <summary>Time spent inside the engines in one read.</summary>
    Time,

    /// <summary>Bytes allocated inside the engines in one read.</summary>
    Memory,

    /// <summary>Recursion depth of the interpreter or the expression parser (also the nesting of an <c>@</c> string), or the stack.</summary>
    Depth,

    /// <summary>Serialized length or nesting of one envelope's result.</summary>
    Result,

    /// <summary>Nested envelope evaluations in one read.</summary>
    EnvelopeDepth,

    /// <summary>Envelope evaluations in one read.</summary>
    EnvelopeCount,

    /// <summary>The length of one <c>@</c> binding string, in UTF-16 code units.</summary>
    ExpressionLength,
}
