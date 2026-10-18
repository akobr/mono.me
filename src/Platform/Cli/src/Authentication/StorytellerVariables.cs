namespace _42.Platform.Cli.Authentication;

// The environment variables of a non-interactive (machine) sign-in. STORYTELLER_API_URL and
// STORYTELLER_API_KEY are the names the admin UI writes into its .env snippet.
public static class StorytellerVariables
{
    public const string API_URL = "STORYTELLER_API_URL";
    public const string PROJECT = "STORYTELLER_PROJECT";
    public const string VIEW = "STORYTELLER_VIEW";

    public const string API_KEY = "STORYTELLER_API_KEY";
    public const string CERTIFICATE_FILE = "STORYTELLER_CERTIFICATE_FILE";
    public const string CERTIFICATE_PASSWORD = "STORYTELLER_CERTIFICATE_PASSWORD";

    public const string CLIENT_ID = "STORYTELLER_CLIENT_ID";
    public const string CLIENT_SECRET = "STORYTELLER_CLIENT_SECRET";
    public const string TOKEN_URL = "STORYTELLER_TOKEN_URL";
    public const string TOKEN_SCOPE = "STORYTELLER_TOKEN_SCOPE";

    public const string SUBJECT_TOKEN_FILE = "STORYTELLER_SUBJECT_TOKEN_FILE";
    public const string SUBJECT_TOKEN_TYPE = "STORYTELLER_SUBJECT_TOKEN_TYPE";

    public const string TOKEN_FILE = "STORYTELLER_TOKEN_FILE";
}
