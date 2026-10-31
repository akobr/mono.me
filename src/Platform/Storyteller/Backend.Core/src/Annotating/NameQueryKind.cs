namespace _42.Platform.Storyteller.Annotating;

public enum NameQueryKind
{
    // The same name, ordinal and case-sensitive.
    Exact,

    // The name contains the text, ordinal and case-sensitive; the query marks it with a leading or trailing %.
    Contains,

    // A regular expression; the query starts with ^ or ends with $.
    Pattern,
}
