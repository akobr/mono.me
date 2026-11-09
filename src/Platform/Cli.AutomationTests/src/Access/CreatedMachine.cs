using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Access;

// A machine a test created with sform account machine create (CliTest.CreateMachineAsync), read from what sform printed
// once: the JSON of the machine (Id, AccessKey, CredentialKind, Scopes), the lines after it ("Certificate written to
// <path>", "Certificate password: …", "Client ID:", "Client secret:", "Token URL:", "Token scope:") and the variables of
// "To sign in sform in a pipeline, set:". Every printed secret is a secret of the test already (SecretScanner).
public sealed record CreatedMachine
{
    public required string Id { get; init; }

    // The printed key: the API key ("2s.…") of an ApiKey or CertificateAndApiKey machine, the client secret of a
    // ClientCredentials one; null for a Certificate or Federated machine.
    public string? AccessKey { get; init; }

    // ApiKey, Certificate, CertificateAndApiKey, ClientCredentials or Federated, as printed.
    public required string CredentialKind { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];

    // The project and view it was created in (-p, -v).
    public required string ProjectKey { get; init; }

    public required string View { get; init; }

    // The full path of the .pfx sform wrote ("Certificate written to <path>", relative to the run's working directory);
    // null when it wrote none (no certificate of its own, a shared certificate).
    public string? CertificatePath { get; init; }

    // "Certificate password: <password>", printed once.
    public string? CertificatePassword { get; init; }

    // The client id of a ClientCredentials machine (its Id).
    public string? ClientId => IsKind("ClientCredentials") ? Id : null;

    // The client secret of a ClientCredentials machine (its AccessKey).
    public string? ClientSecret => IsKind("ClientCredentials") ? AccessKey : null;

    // "Token URL: <url>" of a ClientCredentials machine; null when sform printed only its placeholder.
    public string? TokenUrl { get; init; }

    // "Token scope: <scope>" of a ClientCredentials machine.
    public string? TokenScope { get; init; }

    // The variables of "To sign in sform in a pipeline, set:" as printed (a value may be a placeholder such as
    // "<access key above>"); STORYTELLER_ENDPOINT of a Federated machine is the one its token endpoint needs.
    public IReadOnlyDictionary<string, string> PrintedVariables { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    // The variables to sign in with, for every kind sform printed enough for: ApiKey (the key), Certificate (the .pfx and
    // its password), CertificateAndApiKey with a certificate of its own (both), ClientCredentials (client id, secret,
    // token URL and scope); a CertificateAndApiKey machine of a shared certificate gets its key only (add the shared
    // .pfx with With), a Federated machine none (MachineCredentials.ForFederated with the test's token file).
    public MachineCredentials? Credentials { get; init; }

    public required SformResult Result { get; init; }

    // The credentials of an API key machine; throws for another kind.
    public MachineCredentials ApiKeyCredentials => IsKind("ApiKey") && Credentials is { } credentials
        ? credentials
        : throw new InvalidOperationException($"The machine {Id} ({CredentialKind}) has no API key credentials; use Credentials.");

    // The credentials of any kind; throws when sform printed too little (Federated, see Credentials).
    public MachineCredentials SignInCredentials => Credentials
        ?? throw new InvalidOperationException($"The machine {Id} ({CredentialKind}) has no credentials of its own output; build them with MachineCredentials (ForFederated, FromVariables).");

    private bool IsKind(string kind)
    {
        return string.Equals(CredentialKind, kind, StringComparison.OrdinalIgnoreCase);
    }
}
