namespace _42.Platform.Storyteller.ConfigurationFormats;

// The JSON type of a value. Every value is text; a format may write a number or a boolean without quotes.
public enum DocumentValueKind
{
    // Also any other token built in code (a date, a GUID), as its text.
    String = 0,

    Number,

    Boolean,
}
