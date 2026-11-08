namespace _42.Platform.Storyteller.ConfigurationFormats;

// A text that is not a path in the syntax of JsonPaths. The message says what was expected, never the text itself.
public sealed class JsonPathFormatException : FormatException
{
    public JsonPathFormatException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    // The index of the character where the path is wrong; the length of the text when it ends too early.
    public int Position { get; }
}
