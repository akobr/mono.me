namespace _42.Platform.Cli.AutomationTests.Access;

// The Machines section: the pre-provisioned machines of the target by name (case-insensitive), e.g. Machines:apikey.
// Read with Settings.Bind<MachineCatalog>("Machines"); empty in testsettings.json, filled in testsettings.local.json, a
// profile or UITESTS_Machines__<name>__<Key> with the secrets in the user secrets.
public sealed class MachineCatalog : Dictionary<string, MachineSettings>
{
    public const string Section = "Machines";

    // The API key machine of Target:Project that the machine sign-in cases use (SGN-MSI-*), read scopes at least.
    public const string ApiKeyMachine = "apikey";

    public MachineCatalog()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}
