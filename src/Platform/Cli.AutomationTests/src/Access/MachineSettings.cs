namespace _42.Platform.Cli.AutomationTests.Access;

// One pre-provisioned machine of the target, Machines:<name>: what sform's machine sign-in reads from the STORYTELLER_*
// variables (MachineSignInSettings of src/Platform/Cli, StorytellerCredentialVariables of the SDK). Set the variables of
// one credential kind: ApiKey; Certificate (+ CertificatePassword), optionally with ApiKey; or ClientId and ClientSecret
// (+ ClientCredentialsUrl and ClientCredentialsScope when the provider needs them). The secrets (ApiKey, ClientSecret,
// CertificatePassword) go to the user secrets or UITESTS_Machines__<name>__<Key>, never into a committed file; the
// settings' redaction masks them by their names.
public sealed class MachineSettings
{
    // STORYTELLER_API_KEY: 2s.<machine segment>.<secret> (a secret).
    public string? ApiKey { get; set; }

    // STORYTELLER_CLIENT_ID of client credentials.
    public string? ClientId { get; set; }

    // STORYTELLER_CLIENT_SECRET (a secret).
    public string? ClientSecret { get; set; }

    // STORYTELLER_TOKEN_URL of client credentials, when the id does not tell it.
    public string? ClientCredentialsUrl { get; set; }

    // STORYTELLER_TOKEN_SCOPE of client credentials.
    public string? ClientCredentialsScope { get; set; }

    // STORYTELLER_CLIENT_CERTIFICATE: the path of the machine's .pfx (relative to the test assembly's directory).
    public string? Certificate { get; set; }

    // STORYTELLER_CLIENT_CERTIFICATE_PASSWORD (a secret).
    public string? CertificatePassword { get; set; }

    // STORYTELLER_PROJECT, "<organization>.<project>"; null leaves the project to the credential (an API key names it)
    // or to -p.
    public string? Project { get; set; }

    // STORYTELLER_VIEW; null is the default view.
    public string? View { get; set; }

    // STORYTELLER_KEY, a full key (@org.project.view.key) whose project and view the machine targets.
    public string? Key { get; set; }

    // STORYTELLER_ENDPOINT; null is Target:BaseUrl.
    public string? Endpoint { get; set; }

    // The machine id, for the checks of its sign-in; null: read from the API key's machine segment.
    public string? Id { get; set; }

    // The scopes it was created with (AnnotationRead, ConfigurationWrite, SecretsRead, …); informative, for the docs and
    // the tests that pick a machine by what it may do.
    public List<string> Scopes { get; set; } = [];

    public bool HasCredential => !string.IsNullOrWhiteSpace(ApiKey)
        || !string.IsNullOrWhiteSpace(ClientId)
        || !string.IsNullOrWhiteSpace(Certificate);
}
