// In the root namespace on purpose: every test namespace is below it, so [Area(Area.Annotations)] finds this attribute
// before the library's [Area(string)] that the tests import with _42.Testing.UI.Automation.Runtime.TUnit.
namespace _42.Platform.Cli.AutomationTests;

public sealed class AreaAttribute(Area area) : Testing.UI.Automation.Runtime.TUnit.AreaAttribute(area.ToString());
