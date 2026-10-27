namespace _42.Platform.Storyteller.ConfigurationFormats;

// The complete body of a format: UTF-16 text with LF line ends, to be written as UTF-8 without a byte order mark.
public sealed record FormattedConfiguration(ConfigurationFormat Format, string Text, string MediaType)
{
    // Never the text, which may hold secrets.
    public override string ToString()
    {
        return $"{Format.GetName()} ({MediaType}, {Text.Length} characters)";
    }
}
