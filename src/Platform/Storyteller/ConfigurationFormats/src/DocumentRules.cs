namespace _42.Platform.Storyteller.ConfigurationFormats;

// The rules of InvalidDocumentException, as the test vectors in Configuration/test/Vectors spell them. The base rules
// apply to every format: a document is valid or invalid for the reference provider whatever the format, and a format
// adds rules but never removes one.
public static class DocumentRules
{
    public const string NotAnObject = "not-an-object";
    public const string EmptyName = "empty-name";
    public const string ColonInName = "colon-in-name";
    public const string CaseCollision = "case-collision";

    // properties: Spring's bracket form cannot hold a name with '[' or ']'.
    public const string BracketInName = "bracket-in-name";

    // properties: Spring Boot does not bind a list with a missing index.
    public const string ArrayGap = "array-gap";

    // env: a name outside the variable charset, or one that would blur the '__' delimiter.
    public const string InvalidEnvName = "invalid-env-name";
}
