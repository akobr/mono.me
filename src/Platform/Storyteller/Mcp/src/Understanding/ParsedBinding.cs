namespace _42.Platform.Storyteller.Mcp.Understanding;

// What an @ expression reads, without evaluating it. Name is the function or source of a call.
public sealed record ParsedBinding(
    BindingForm Form,
    string? Name,
    IReadOnlyList<BindingReference> References,
    bool ReadsDocument,
    bool ReadsAnnotations);
