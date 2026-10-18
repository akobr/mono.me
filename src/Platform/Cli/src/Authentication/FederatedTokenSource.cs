namespace _42.Platform.Cli.Authentication;

// Where a federated machine gets the platform token it exchanges for a Storyteller token.
public enum FederatedTokenSource
{
    // STORYTELLER_SUBJECT_TOKEN_FILE, read again before every exchange.
    File = 0,

    // The ID token of the GitHub Actions job, requested again before every exchange.
    GitHubActions,
}
