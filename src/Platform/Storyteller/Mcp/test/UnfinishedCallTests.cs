using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Polly.Timeout;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// A call to the API that did not finish may have completed on the server: the tool says so instead of a generic error.
public sealed class UnfinishedCallTests : IDisposable
{
    private const string Marker = "TRANSPORT-MARKER-5e1d";

    private readonly McpTestHost _host = new();

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "connection", new HttpRequestException($"Connection reset ({Marker}).") },
        { "attempt timeout", new TimeoutRejectedException($"Timed out ({Marker}).") },
        { "HttpClient timeout", new TaskCanceledException($"Timed out ({Marker}).", new TimeoutException()) },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task CreateMachine_ThatDoesNotFinish_SaysItMayHaveCompleted(string because, Exception failure)
    {
        _host.Api.Respond = _ => throw failure;
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("create_machine", new { organization = "house", project = "main", scopes = new[] { "AnnotationRead" } });

        result.IsError.ShouldBe(true, because);
        result.Text().ShouldBe(StorytellerCallFilter.UnfinishedCallMessage, because);
        result.Text().ShouldContain("list_machines", Case.Sensitive, because);
        _host.Api.Requests.Count.ShouldBe(1, because);

        // The filter logs the tool and the exception type, never the exception.
        var logged = _host.Logs.Entries.Where(entry => entry.Contains($" {StorytellerCallFilter.LoggerCategory}: ", StringComparison.Ordinal)).ToList();
        logged.ShouldContain(entry => entry.Contains($"Tool create_machine: a call to the Storyteller API did not finish ({failure.GetType().Name}).", StringComparison.Ordinal), because);
        logged.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal), because);
    }

    [Fact]
    public void Message_NamesTheMatchingReads()
    {
        foreach (var read in new[] { "list_machines", "get_annotation", "get_configuration", "get_key_value" })
        {
            StorytellerCallFilter.UnfinishedCallMessage.ShouldContain(read);
        }

        StorytellerCallFilter.UnfinishedCallMessage.ShouldContain("may have completed on the server");
    }

    [Fact]
    public void CallersCancellation_StaysACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        StorytellerCallFilter.IsUnfinishedCall(new OperationCanceledException(cancelled.Token), cancelled.Token).ShouldBeFalse();
        StorytellerCallFilter.IsUnfinishedCall(new TaskCanceledException(), cancelled.Token).ShouldBeFalse();
        StorytellerCallFilter.IsUnfinishedCall(new TaskCanceledException(), CancellationToken.None).ShouldBeTrue();
    }

    [Fact]
    public void OtherFailures_AreNotUnfinishedCalls()
    {
        StorytellerCallFilter.IsUnfinishedCall(new InvalidOperationException(), CancellationToken.None).ShouldBeFalse();
        StorytellerCallFilter.IsUnfinishedCall(ToolFixtures.NotFound(), CancellationToken.None).ShouldBeFalse();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
