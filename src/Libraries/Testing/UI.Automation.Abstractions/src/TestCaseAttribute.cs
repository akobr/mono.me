namespace _42.Testing.UI.Automation;

// Links a test to a case of a test case management system, e.g. [TestCase("1234")] for Testiny. A test may link
// several cases and several tests may link one case; on a class it applies to every test of the class. The engine
// adapter reads it from the method and the class, and a reporter aggregates the results per case.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class TestCaseAttribute(string id) : Attribute
{
    // The id in the external system, e.g. "1234" or "TC-1234"; how it is read is up to the reporter.
    public string Id { get; } = id;

    // Restricts the id to one reporter ("Testiny", "TestRail") when several report the same run; null for all of them.
    public string? System { get; init; }

    public TestCaseLink ToLink()
    {
        return new TestCaseLink(Id, System);
    }
}
