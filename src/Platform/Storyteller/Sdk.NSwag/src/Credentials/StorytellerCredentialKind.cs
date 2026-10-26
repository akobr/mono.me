namespace _42.Platform.Storyteller.Sdk.Credentials;

// The kind of credential the STORYTELLER_* variables describe.
public enum StorytellerCredentialKind
{
    // STORYTELLER_API_KEY.
    ApiKey = 0,

    // STORYTELLER_CLIENT_CERTIFICATE alone.
    Certificate,

    // STORYTELLER_CLIENT_CERTIFICATE with STORYTELLER_API_KEY.
    CertificateAndApiKey,

    // STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET.
    ClientCredentials,

    // STORYTELLER_TOKEN_FILE without STORYTELLER_CLIENT_ID: a bearer token the API accepts.
    TokenFile,

    // STORYTELLER_CLIENT_ID with a platform token to exchange: a file, or the GitHub Actions runner.
    Federated,
}
