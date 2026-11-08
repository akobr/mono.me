namespace _42.Platform.Cli.AutomationTests.SelfTests;

// What StubApi answers to a request.
internal sealed record StubResponse(int Status, string Body)
{
    public static StubResponse NotFound { get; } = new(404, """{ "Message": "not in the stub" }""");

    public static StubResponse Json(string body) => new(200, body);
}
