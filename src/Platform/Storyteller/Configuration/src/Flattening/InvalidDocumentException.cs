namespace _42.Platform.Storyteller.Sdk.Configuring.Flattening;

// A document that cannot become IConfiguration keys; it is never applied. Names the rule and the JSON path, never a
// value.
internal sealed class InvalidDocumentException : Exception
{
    public InvalidDocumentException(string rule, string path, string message)
        : base(message)
    {
        Rule = rule;
        Path = path;
    }

    // One of the DocumentFlattener rule names, as the test vectors spell them.
    public string Rule { get; }

    public string Path { get; }
}
