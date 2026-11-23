using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

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
        { "response body ended early", EndedEarly() },
        { "connection reset during the body", Reset() },
    };

    // How SocketsHttpHandler reports a body read that breaks: an early end of the connection, or a reset (RST).
    public static TheoryData<string, Exception> BodyFailures() => new()
    {
        { "ended early", EndedEarly() },
        { "reset", Reset() },
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

    // The one-time credentials of a new machine arrive in the body: a body that breaks while it is read is a call that
    // may have completed, not a generic error. The SDK reports it as an ApiException with the status 200.
    [Theory]
    [MemberData(nameof(BodyFailures))]
    public async Task CreateMachine_WhoseAnswerBreaksWhileItIsRead_SaysItMayHaveCompleted(string because, Exception failure)
    {
        _host.Api.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BrokenBodyStream(failure)) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("create_machine", new { organization = "house", project = "main", scopes = new[] { "AnnotationRead" } });

        result.IsError.ShouldBe(true, because);
        result.Text().ShouldBe(StorytellerCallFilter.UnfinishedCallMessage, because);
        _host.Api.Requests.Count.ShouldBe(1, because);
        _host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool create_machine: a call to the Storyteller API did not finish ({failure.GetType().Name}).", StringComparison.Ordinal), because);
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal), because);
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

        // Only the network's IOExceptions: one of a file is not a call that did not finish.
        StorytellerCallFilter.IsUnfinishedCall(new FileNotFoundException(), CancellationToken.None).ShouldBeFalse();
        StorytellerCallFilter.IsUnfinishedCall(new IOException("disk"), CancellationToken.None).ShouldBeFalse();
        StorytellerCallFilter.IsUnfinishedCall(ToolFixtures.NotFound(), CancellationToken.None).ShouldBeFalse();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static HttpIOException EndedEarly() => new(HttpRequestError.ResponseEnded, $"The response ended early ({Marker}).");

    private static IOException Reset() => new($"Unable to read data from the transport connection ({Marker}).", new SocketException((int)SocketError.ConnectionReset));

    // The start of an answer, then the failure of the connection.
    private sealed class BrokenBodyStream(Exception failure) : Stream
    {
        private readonly byte[] _start = Encoding.UTF8.GetBytes("""{"Machine":{"Id":"m-""");
        private bool _started;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_started)
            {
                throw failure;
            }

            _started = true;
            var length = Math.Min(count, _start.Length);
            Array.Copy(_start, 0, buffer, offset, length);
            return length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
