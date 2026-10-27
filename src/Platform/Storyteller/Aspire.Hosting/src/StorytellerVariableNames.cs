namespace _42.Platform.Storyteller.AspireHosting;

// The STORYTELLER_* names the resource writes, the contract every SDK and sform read (424-96). Copied from the SDK's
// StorytellerVariables, so an AppHost does not reference the SDK; a test compares the two, so a new SDK credential name
// fails until it is mapped here or listed as not offered.
internal static class StorytellerVariableNames
{
    public const string ENDPOINT = "STORYTELLER_ENDPOINT";
    public const string KEY = "STORYTELLER_KEY";

    public const string API_KEY = "STORYTELLER_API_KEY";
    public const string CLIENT_ID = "STORYTELLER_CLIENT_ID";
    public const string CLIENT_SECRET = "STORYTELLER_CLIENT_SECRET";
    public const string TOKEN_URL = "STORYTELLER_TOKEN_URL";
    public const string TOKEN_SCOPE = "STORYTELLER_TOKEN_SCOPE";
    public const string CLIENT_CERTIFICATE = "STORYTELLER_CLIENT_CERTIFICATE";
    public const string CLIENT_CERTIFICATE_PASSWORD = "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD";

    // Every credential name the resource can write. No token file or federation: processes started by an AppHost have
    // no projected or CI token. No deprecated alias: the canonical names are read first.
    public static IReadOnlyList<string> Credentials { get; } =
    [
        API_KEY,
        CLIENT_ID,
        CLIENT_SECRET,
        TOKEN_URL,
        TOKEN_SCOPE,
        CLIENT_CERTIFICATE,
        CLIENT_CERTIFICATE_PASSWORD,
    ];
}
