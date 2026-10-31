namespace _42.Testing.UI.Automation;

// One [TestCase] of a test. System null: the id is meant for every reporter.
public sealed record TestCaseLink(string Id, string? System)
{
    public bool AppliesTo(string system)
    {
        return System is null || string.Equals(System, system, StringComparison.OrdinalIgnoreCase);
    }
}
