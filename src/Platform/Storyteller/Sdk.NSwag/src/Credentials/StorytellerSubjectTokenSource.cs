namespace _42.Platform.Storyteller.Sdk.Credentials;

// Where a federated credential gets the platform token it exchanges for a Storyteller token.
public enum StorytellerSubjectTokenSource
{
    // STORYTELLER_TOKEN_FILE (or the deprecated STORYTELLER_SUBJECT_TOKEN_FILE), read again before every exchange.
    File = 0,

    // The ID token of the GitHub Actions job, requested again before every exchange.
    GitHubActions,
}
