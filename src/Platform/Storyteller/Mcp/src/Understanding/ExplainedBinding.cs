namespace _42.Platform.Storyteller.Mcp.Understanding;

// A binding of the effective document as stored, with what it reads. EnvelopePointer names the object binding whose
// $context holds it.
public sealed record ExplainedBinding
{
    public required string Pointer { get; init; }

    public required string Expression { get; init; }

    public required BindingForm Form { get; init; }

    public string? Name { get; init; }

    public bool IsValid { get; init; }

    public string? EnvelopePointer { get; init; }

    public required IReadOnlyList<Reference> References { get; init; }

    public IReadOnlyList<string>? Reads { get; init; }

    public string? Note { get; init; }

    // A key the binding reads. KeyValue is the pair's metadata for the Values store (default, public and secret).
    public sealed record Reference
    {
        public required string Key { get; init; }

        public required string Source { get; init; }

        public bool IsValuesStore { get; init; }

        public KeyValueMetadata? KeyValue { get; init; }

        public string? Note { get; init; }
    }
}
