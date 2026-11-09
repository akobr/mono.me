using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [MachineOnly] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test that runs sform only as a machine (mode M, role Machine of the catalogue): the category MachineOnly, for which
// CliTest leases no pool user (LeasesUser is false), so it never waits for one and never signs one in. Its runs use
// CliTest.AsMachine("<name>") or a machine the test created through another means.
public sealed class MachineOnlyAttribute() : CategoryAttribute(CliCategories.MachineOnly);
