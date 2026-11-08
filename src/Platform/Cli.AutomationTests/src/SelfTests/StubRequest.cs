namespace _42.Platform.Cli.AutomationTests.SelfTests;

// A request StubApi received, and the status it answered.
internal sealed record StubRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public int Status { get; init; }
}
