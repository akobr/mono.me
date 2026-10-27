namespace _42.Platform.Storyteller.AspireHosting;

// The STORYTELLER_* names the SDK reads for who a process is and how it signs in, the contract every SDK and sform read
// (424-96). Copied from the SDK's StorytellerVariables, so an AppHost does not reference the SDK; a test compares the
// two.
internal static class StorytellerVariableNames
{
    public const string ENDPOINT = "STORYTELLER_ENDPOINT";

    // Deprecated: ENDPOINT.
    public const string API_URL = "STORYTELLER_API_URL";

    public const string KEY = "STORYTELLER_KEY";

    // The credential names a resource sets: WithApiKey, WithClientCredentials and WithClientCertificate.
    public const string API_KEY = "STORYTELLER_API_KEY";
    public const string CLIENT_ID = "STORYTELLER_CLIENT_ID";
    public const string CLIENT_SECRET = "STORYTELLER_CLIENT_SECRET";
    public const string TOKEN_URL = "STORYTELLER_TOKEN_URL";
    public const string TOKEN_SCOPE = "STORYTELLER_TOKEN_SCOPE";
    public const string CLIENT_CERTIFICATE = "STORYTELLER_CLIENT_CERTIFICATE";
    public const string CLIENT_CERTIFICATE_PASSWORD = "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD";

    // The credential names a resource never sets: processes started by an AppHost have no projected or CI token, and the
    // canonical names are read before the deprecated ones. WithReference writes them empty.
    public const string CERTIFICATE_FILE = "STORYTELLER_CERTIFICATE_FILE";
    public const string CERTIFICATE_PASSWORD = "STORYTELLER_CERTIFICATE_PASSWORD";
    public const string TOKEN_FILE = "STORYTELLER_TOKEN_FILE";
    public const string SUBJECT_TOKEN_FILE = "STORYTELLER_SUBJECT_TOKEN_FILE";
    public const string SUBJECT_TOKEN_TYPE = "STORYTELLER_SUBJECT_TOKEN_TYPE";

    // Every name that makes a credential, deprecated ones included, as the SDK's StorytellerVariables.Credentials.
    public static IReadOnlyList<string> Credentials { get; } =
    [
        API_KEY,
        CLIENT_CERTIFICATE,
        CERTIFICATE_FILE,
        CLIENT_CERTIFICATE_PASSWORD,
        CERTIFICATE_PASSWORD,
        CLIENT_ID,
        CLIENT_SECRET,
        TOKEN_URL,
        TOKEN_SCOPE,
        TOKEN_FILE,
        SUBJECT_TOKEN_FILE,
        SUBJECT_TOKEN_TYPE,
    ];

    // Every name WithReference writes to a process that inherits the AppHost's environment: the endpoint, its
    // deprecated name, the key and every credential name, each empty when nothing sets it.
    public static IReadOnlyList<string> Identity { get; } = [ENDPOINT, API_URL, KEY, .. Credentials];
}
