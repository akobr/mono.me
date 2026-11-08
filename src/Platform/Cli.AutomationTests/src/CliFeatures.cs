namespace _42.Platform.Cli.AutomationTests;

// The names of Target:Features, which [RequiresFeature("<name>")] asks for (mode X of the catalogue).
public static class CliFeatures
{
    // A customer key in an Azure Key Vault (BYOK): KeyValues:ByokKeyUri and KeyValues:ByokTenantId (KeyValueSettings).
    public const string Byok = "Byok";

    // HSM-backed keys.
    public const string Hsm = "Hsm";

    // An unregistered pool user whose account the run may create (account register).
    public const string Registration = "Registration";

    // A pool user in two WorkOS organizations.
    public const string MultiOrganization = "MultiOrganization";

    public static IReadOnlyList<string> All { get; } = [Byok, Hsm, Registration, MultiOrganization];

    // The features a pool user stands for: the target marks that user with the feature's name among its Roles in
    // Users:Pool (e.g. { "Email": "…", "Roles": [ "Reader", "Registration" ] }). A [RequiresFeature] test of such a feature
    // leases that user (CliTest.FeatureUser), and no other test ever leases it (CliTest.WithoutFeatureUsers).
    public static IReadOnlyList<string> OfUsers { get; } = [Registration, MultiOrganization];
}
