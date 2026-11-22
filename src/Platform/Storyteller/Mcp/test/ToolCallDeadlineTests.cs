using System.Diagnostics;

using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// A tool call ends at its deadline with the server's own answer while the client still waits for one: before the
// Container Apps ingress drops the request (240 seconds without a byte) and before the API's front end gives up (230
// seconds). The deadline covers the token exchange and every call of the API together.
public sealed class ToolCallDeadlineTests : IDisposable
{
    private const string Key = "exe.northwind.billing.prod";

    private const string AnnotationJson =
        """{"AnnotationType":"Execution","ProjectName":"main","ViewName":"default","AnnotationKey":"exe.northwind.billing.prod","Name":"prod"}""";

    // How long a test waits in real time for what the fake clock sets off.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // How long the stubbed exchange takes on the fake clock.
    private static readonly TimeSpan ExchangeTime = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly McpTestHost _host;

    public ToolCallDeadlineTests()
    {
        _host = new McpTestHost(clock: _clock);

        // An exchange takes a moment, so the API call's own timeouts start after the deadline, as they do in production.
        _host.Tokens.Exchanging = () => _clock.Advance(ExchangeTime);
    }

    [Theory]
    [InlineData("get_annotation")]
    [InlineData("create_machine")]
    public async Task CallThatDoesNotAnswer_EndsAtTheDeadline(string tool)
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        _host.Api.RespondAsync = async (_, cancellationToken) =>
        {
            arrived.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }

            throw new UnreachableException();
        };
        await using var client = await _host.ConnectAsync();

        var call = client.CallAsync(tool, Arguments(tool));
        await arrived.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        // Exactly to the deadline: the API call's own timeouts, started a second later, have not run out.
        _clock.Advance(StorytellerCallFilter.ToolCallTimeout - ExchangeTime);
        var result = await call.WaitAsync(Wait, TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(StorytellerCallFilter.ToolCallTimeoutMessage);
        cancelled.ShouldBeTrue();

        // A GET is not retried once the deadline has passed.
        _host.Api.Requests.Count.ShouldBe(1);

        // The filter logs the tool, the deadline and the exception type, never the exception.
        var logged = _host.Logs.Entries.Where(entry => entry.Contains($" {StorytellerCallFilter.LoggerCategory}: ", StringComparison.Ordinal)).ToList();
        logged.ShouldContain(entry => entry.Contains($"Tool {tool}: stopped at the deadline of a tool call, 200 seconds (", StringComparison.Ordinal));
        logged.ShouldAllBe(entry => entry.TrimEnd().EndsWith(").", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallsThatEachFit_EndAtTheDeadlineOfTheWholeToolCall()
    {
        // set_configuration checks the annotation, then writes: 120 seconds each, together past the deadline.
        _host.Api.RespondAsync = async (request, cancellationToken) =>
        {
            _clock.Advance(TimeSpan.FromSeconds(120));

            if (request.Method == HttpMethod.Get)
            {
                return RecordingApiHandler.Json(AnnotationJson);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        };
        await using var client = await _host.ConnectAsync();

        var result = await client
            .CallAsync("set_configuration", new { organization = "house", project = "main", key = Key, content = new { retries = 3 } })
            .WaitAsync(Wait, TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(StorytellerCallFilter.ToolCallTimeoutMessage);
        var requests = _host.Api.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0].Method.ShouldBe(HttpMethod.Get);
        requests[1].Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task ToolCall_ThatEndsBeforeTheDeadline_Answers()
    {
        // With the exchange, a second before the deadline: the call's token is not cancelled yet.
        var cancelled = true;
        _host.Api.RespondAsync = (_, cancellationToken) =>
        {
            _clock.Advance(StorytellerCallFilter.ToolCallTimeout - ExchangeTime - TimeSpan.FromSeconds(1));
            cancelled = cancellationToken.IsCancellationRequested;
            return Task.FromResult(RecordingApiHandler.Json(AnnotationJson));
        };
        await using var client = await _host.ConnectAsync();

        var result = await client
            .CallAsync("get_annotation", Arguments("get_annotation"))
            .WaitAsync(Wait, TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true, result.Text());
        result.Text().ShouldContain(Key);
        cancelled.ShouldBeFalse();
    }

    [Fact]
    public void Message_SaysTheLimitAndNamesTheMatchingReads()
    {
        StorytellerCallFilter.ToolCallTimeoutMessage.ShouldStartWith("The tool call was stopped after 200 seconds");

        foreach (var read in new[] { "list_machines", "get_annotation", "get_configuration", "get_key_value" })
        {
            StorytellerCallFilter.ToolCallTimeoutMessage.ShouldContain(read);
        }

        StorytellerCallFilter.ToolCallTimeoutMessage.ShouldContain("may have completed on the server");
        StorytellerCallFilter.ToolCallTimeoutMessage.ShouldContain("reset the machine");
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static object Arguments(string tool) => tool switch
    {
        "get_annotation" => new { organization = "house", project = "main", key = Key },
        "create_machine" => new { organization = "house", project = "main", scopes = new[] { "AnnotationRead" } },
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };
}
