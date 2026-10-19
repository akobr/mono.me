using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PostHog;
using PostHog.Features;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The event of a run: an allowlist of properties, sent within a time limit that a broken network cannot stretch.
public class PostHogCommandTelemetryTests
{
    private const string InstallationId = "6d0f2c4e00004000800000000000000a";

    private static readonly string[] AlwaysSent =
    [
        "command", "outcome", "exit_code", "duration_ms", "cli_version", "os", "os_architecture", "runtime",
        "environment", "ci", "machine_sign_in", "$process_person_profile", "$geoip_disable",
    ];

    // The longest a run waits for the event, with room for a slow test machine.
    private static readonly TimeSpan Limit = PostHogCommandTelemetry.FlushTimeout + PostHogCommandTelemetry.DisposeTimeout + TimeSpan.FromSeconds(1);

    private readonly Mock<IPostHogClient> _client = new();
    private readonly List<(string DistinctId, string EventName, Dictionary<string, object> Properties)> _captured = [];

    public PostHogCommandTelemetryTests()
    {
        _client
            .Setup(client => client.Capture(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<GroupCollection?>(),
                It.IsAny<FeatureFlagEvaluations?>(),
                It.IsAny<DateTimeOffset?>()))
            .Callback((string distinctId, string eventName, Dictionary<string, object>? properties, GroupCollection? _, FeatureFlagEvaluations? _, DateTimeOffset? _) =>
                _captured.Add((distinctId, eventName, properties!)))
            .Returns(true);
        _client.Setup(client => client.FlushAsync()).Returns(Task.CompletedTask);
        _client.Setup(client => client.DisposeAsync()).Returns(ValueTask.CompletedTask);
    }

    [Fact]
    public async Task ARun_IsOneEventOfTheInstallation_WithExactlyTheAllowlistedProperties()
    {
        var run = new CommandRun { Command = "sform storyteller kv get" };

        await Telemetry(isContinuousIntegration: true).SendAsync(run, ExitCodes.SUCCESS);

        var (distinctId, eventName, properties) = _captured.ShouldHaveSingleItem();
        distinctId.ShouldBe(InstallationId);
        eventName.ShouldBe("cli_command_executed");
        properties.Keys.ShouldBe(AlwaysSent, ignoreOrder: true);
        properties["command"].ShouldBe("sform storyteller kv get");
        properties["outcome"].ShouldBe(CommandOutcome.SUCCESS);
        properties["exit_code"].ShouldBe(ExitCodes.SUCCESS);
        properties["os"].ShouldBeOneOf("windows", "linux", "macos", "other");
        properties["runtime"].ShouldBe(Environment.Version.ToString());
        properties["environment"].ShouldBe("Production");
        properties["ci"].ShouldBe(true);
        properties["machine_sign_in"].ShouldBe(false);
        properties["$process_person_profile"].ShouldBe(false);
        properties["$geoip_disable"].ShouldBe(true);
        properties["cli_version"].ShouldBe(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        _client.Verify(client => client.FlushAsync(), Times.Once);
        _client.Verify(client => client.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task AFailedSignIn_AddsTheExceptionTypeAndTheReason_NeverTheMessage()
    {
        var run = new CommandRun
        {
            Command = "sform account",
            Exception = new AuthenticationException(AuthenticationFailureReason.Configuration, "STORYTELLER_PROJECT must be acme.secret-project."),
        };

        await Telemetry(isMachineSignIn: true).SendAsync(run, ExitCodes.ERROR_WRONG_INPUT);

        var properties = _captured.ShouldHaveSingleItem().Properties;
        properties.Keys.ShouldBe(AlwaysSent.Append("exception").Append("authentication_failure"), ignoreOrder: true);
        properties["exception"].ShouldBe(nameof(AuthenticationException));
        properties["authentication_failure"].ShouldBe(nameof(AuthenticationFailureReason.Configuration));
        properties["outcome"].ShouldBe(CommandOutcome.ERROR);
        properties["machine_sign_in"].ShouldBe(true);
        Values(properties).ShouldNotContain(value => value.Contains("secret-project"));
    }

    [Fact]
    public async Task ARefusedApiCall_AddsTheStatus()
    {
        var run = new CommandRun
        {
            Command = "sform storyteller kv get",
            Exception = new ApiException("Forbidden", 403, "{\"key\":\"db.password\"}", new Dictionary<string, IEnumerable<string>>(), null),
        };

        await Telemetry().SendAsync(run, ExitCodes.WARNING_UNAUTHORIZED_ACCESS);

        var properties = _captured.ShouldHaveSingleItem().Properties;
        properties.Keys.ShouldBe(AlwaysSent.Append("exception").Append("api_status"), ignoreOrder: true);
        properties["exception"].ShouldBe(nameof(ApiException));
        properties["api_status"].ShouldBe(403);
        properties["outcome"].ShouldBe(CommandOutcome.WARNING);
        Values(properties).ShouldNotContain(value => value.Contains("db.password"));
    }

    // The generated client throws ApiException<ErrorResponse> for a documented error body: the name, not "ApiException`1".
    [Fact]
    public async Task ARefusedApiCallWithAnErrorBody_ReportsTheTypeWithoutTheGenericArity()
    {
        var run = new CommandRun
        {
            Command = "sform storyteller kv get",
            Exception = new ApiException<ErrorResponse>(
                "Not Found",
                404,
                "{\"message\":\"db.password\"}",
                new Dictionary<string, IEnumerable<string>>(),
                new ErrorResponse { ErrorCode = "NotFound", Message = "Key db.password was not found." },
                null),
        };

        await Telemetry().SendAsync(run, ExitCodes.ERROR_WRONG_INPUT);

        var properties = _captured.ShouldHaveSingleItem().Properties;
        properties.Keys.ShouldBe(AlwaysSent.Append("exception").Append("api_status"), ignoreOrder: true);
        properties["exception"].ShouldBe(nameof(ApiException));
        properties["api_status"].ShouldBe(404);
        Values(properties).ShouldNotContain(value => value.Contains("db.password"));
    }

    // The arguments of the run below: a key name, an option value and a path. None may reach the event.
    [Fact]
    public async Task NoArgumentOrOptionValue_ReachesTheEvent()
    {
        var app = new CommandLineApplication { Name = "sform" };
        var get = app.Command("get", command => command.Argument("key", "The key."));
        var values = new[] { "db.password", "acme.billing", "C:\\secrets\\out.json" };
        var run = new CommandRun();
        run.RecordParsing(app.Parse("get", values[0]).SelectedCommand);
        run.Exception = new CommandParsingException(get, $"Unrecognized option '--project {values[1]}' for '{values[2]}'");

        await Telemetry().SendAsync(run, ExitCodes.ERROR_INPUT_PARSING);

        var properties = _captured.ShouldHaveSingleItem().Properties;
        properties["command"].ShouldBe("sform get");
        properties["outcome"].ShouldBe(CommandOutcome.INPUT_ERROR);
        properties["exception"].ShouldBe(nameof(CommandParsingException));
        Values(properties).ShouldAllBe(value => values.All(argument => !value.Contains(argument)));
    }

    [Fact]
    public async Task AFlushThatNeverEnds_EndsTheSendWithinTheLimit()
    {
        _client.Setup(client => client.FlushAsync()).Returns(new TaskCompletionSource().Task);

        var elapsed = await TimeSendAsync();

        elapsed.ShouldBeLessThan(Limit);
        _client.Verify(client => client.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task AFlushThatThrows_AndADisposeThatNeverEnds_EndTheSendWithinTheLimit()
    {
        _client.Setup(client => client.FlushAsync()).ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        _client.Setup(client => client.DisposeAsync()).Returns(new ValueTask(new TaskCompletionSource().Task));

        var elapsed = await TimeSendAsync();

        elapsed.ShouldBeLessThan(Limit);
    }

    [Fact]
    public async Task ACaptureThatThrows_StillDisposesTheClient()
    {
        _client
            .Setup(client => client.Capture(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<GroupCollection?>(),
                It.IsAny<FeatureFlagEvaluations?>(),
                It.IsAny<DateTimeOffset?>()))
            .Throws(new ObjectDisposedException("PostHogClient"));

        await Should.NotThrowAsync(() => Telemetry().SendAsync(new CommandRun(), ExitCodes.SUCCESS));

        _client.Verify(client => client.FlushAsync(), Times.Never);
        _client.Verify(client => client.DisposeAsync(), Times.Once);
    }

    private static IEnumerable<string> Values(Dictionary<string, object> properties)
    {
        return properties.Values.Select(value => Convert.ToString(value) ?? string.Empty);
    }

    private PostHogCommandTelemetry Telemetry(bool isContinuousIntegration = false, bool isMachineSignIn = false)
    {
        return new PostHogCommandTelemetry(_client.Object, InstallationId, "Production", isContinuousIntegration, isMachineSignIn, NullLogger.Instance);
    }

    private async Task<TimeSpan> TimeSendAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await Should.NotThrowAsync(() => Telemetry().SendAsync(new CommandRun { Command = "sform" }, ExitCodes.SUCCESS));
        return stopwatch.Elapsed;
    }
}
