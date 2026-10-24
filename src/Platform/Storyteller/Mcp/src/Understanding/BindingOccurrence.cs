namespace _42.Platform.Storyteller.Mcp.Understanding;

// A binding found in a document. Expression is the string as stored, or the $binding kind of an object binding.
// EnvelopePointer names the object binding whose $context holds this string; the resolver replaces that whole object.
public sealed record BindingOccurrence(
    string Pointer,
    string Expression,
    ParsedBinding Parsed,
    bool IsValid,
    string? EnvelopePointer);
