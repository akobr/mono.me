namespace _42.Platform.Storyteller.ConfigurationFormats;

// A value of a document that makes a key: the steps to it, its text (a string's value, a number's or a boolean's JSON
// text), its JSON type and its JSON path. Every format renders from these leaves, so a value present in one format is
// present in all of them.
public sealed record DocumentLeaf(IReadOnlyList<DocumentSegment> Segments, string Value, DocumentValueKind Kind, string Path)
{
    // Never the value, which may be a secret.
    public override string ToString()
    {
        return $"{Path} ({Kind})";
    }
}
