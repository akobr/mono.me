namespace _42.Testing.UI.Automation;

// A test as the engine discovered it. Id is stable across runs and unique per test instance (<Assembly>/<Class>/<Method>,
// plus the display name of a data-driven case and, for a test that runs as several instances, a short hash of the
// engine's own stable id); DisplayName is what the engine shows, e.g. "Data(1, one)". ClassName is the full name.
public sealed record TestIdentity(
    string Id,
    string DisplayName,
    string ClassName,
    string MethodName,
    string Assembly,
    IReadOnlyList<string> Categories,
    IReadOnlyList<TestCaseLink> Cases);
