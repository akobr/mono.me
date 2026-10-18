namespace _42.Platform.Cli.Authentication;

// The variables a GitHub Actions job with permissions: id-token: write gets from the runner.
public static class GitHubActionsVariables
{
    public const string ID_TOKEN_REQUEST_URL = "ACTIONS_ID_TOKEN_REQUEST_URL";
    public const string ID_TOKEN_REQUEST_TOKEN = "ACTIONS_ID_TOKEN_REQUEST_TOKEN";
}
