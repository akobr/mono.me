namespace _42.Platform.Cli.Authentication;

// The machine credential a set of STORYTELLER_* variables describes.
public enum MachineSignInKind
{
    ApiKey = 0,
    Certificate,
    CertificateAndApiKey,
    ClientCredentials,
    TokenFile,
    Federated,
}
