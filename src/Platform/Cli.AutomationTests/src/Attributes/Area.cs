// In the root namespace like AreaAttribute, so a test writes [Area(Area.Annotations)] without a using.
namespace _42.Platform.Cli.AutomationTests;

// The sections of the catalogue (docs/Platform/Cli.AutomationTests/test-cases.md), one folder of Tests/ each;
// [Area(Area.KeyValues)] is the category "Area-KeyValues".
public enum Area
{
    SignIn,
    Account,
    Machines,
    SharedCertificates,
    Annotations,
    Configurations,
    Templates,
    Schemas,
    KeyValues,
    General,
}
