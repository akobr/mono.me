using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [RequiresMachine(MachineCatalog.ApiKeyMachine)] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test that signs in as a pre-provisioned machine of the target (CliTest.AsMachine("<name>")): the category
// Machine-<name>, which CliTest.GetSkipReason skips before the lease, with the setting to add, when Machines:<name> is not
// configured with a credential.
public sealed class RequiresMachineAttribute(string name) : CategoryAttribute(MachinePrefix + name)
{
    public const string MachinePrefix = "Machine-";

    public string Name { get; } = name;
}
