namespace _42.Platform.Storyteller.Mcp.Understanding;

// A binding found in a document. Expression is the string as stored, or the $binding kind of an object binding.
// EnvelopePointer names the object binding whose $context or $definition holds this string; the resolver replaces that
// whole object. InDefinition: the string is in a $definition, the program, and is read only if the program emits it,
// because the resolver binds the program's result again after the evaluation.
public sealed record BindingOccurrence(
    string Pointer,
    string Expression,
    ParsedBinding Parsed,
    bool IsValid,
    string? EnvelopePointer,
    bool InDefinition = false);
