namespace _42.Platform.Cli.AutomationTests;

// The names of Target:Features, which [RequiresFeature("<name>")] asks for (mode X of the catalogue).
public static class CliFeatures
{
    // A customer key in an Azure Key Vault (BYOK).
    public const string Byok = "Byok";

    // HSM-backed keys.
    public const string Hsm = "Hsm";

    // An unregistered pool user whose account the run may create (account register).
    public const string Registration = "Registration";

    // A pool user in two WorkOS organizations.
    public const string MultiOrganization = "MultiOrganization";

    public static IReadOnlyList<string> All { get; } = [Byok, Hsm, Registration, MultiOrganization];
}
