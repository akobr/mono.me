namespace _42.Platform.Storyteller.ConfigurationFormats;

// A document that cannot become IConfiguration keys, or the keys of a format; it is never applied, and nothing is
// written for it. Names the rule and the JSON path, never a value.
public sealed class InvalidDocumentException : Exception
{
    public InvalidDocumentException(string rule, string path, string message)
        : base(message)
    {
        Rule = rule;
        Path = path;
    }

    // One of DocumentRules, as the test vectors spell them.
    public string Rule { get; }

    public string Path { get; }
}
