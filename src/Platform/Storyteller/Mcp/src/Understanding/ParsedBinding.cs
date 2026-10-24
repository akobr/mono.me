namespace _42.Platform.Storyteller.Mcp.Understanding;

// What an @ expression reads, without evaluating it. Name is the function or source of a call. ExceedsLimits: the
// expression is longer or nested deeper than the reader follows (BindingExpressions.MaxLength and MaxDepth), so it was
// not read and is Invalid.
public sealed record ParsedBinding(
    BindingForm Form,
    string? Name,
    IReadOnlyList<BindingReference> References,
    bool ReadsDocument,
    bool ReadsAnnotations,
    bool ExceedsLimits = false);
