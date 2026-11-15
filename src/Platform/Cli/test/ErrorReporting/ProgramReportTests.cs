using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.ErrorReporting;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Cli.UnitTests.Telemetry;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// What Main reports of a failed run, through Program.RunAsync with its own run and reporter: to Sentry the command path
// and the option names, never a value given on the command line; to the local log no secret.
public class ProgramReportTests
{
    private readonly RecordingTransport _transport = new();
    private readonly RecordingLogger _logger = new();
    private readonly CommandRun _run = new();
    private readonly ErrorReporter _reporter;

    public ProgramReportTests()
    {
        _reporter = ErrorReporter.Create(_logger, ErrorReporterTests.Settings(), options => options.Transport = _transport);
    }

    [Fact]
    public async Task ASecretValue_ReachesNeitherSentryNorTheLog()
    {
        string[] args = ["story", "kv", "set", "db.password", "s3cr3t-value", "--secret", "-p", "acme.billing"];

        var exitCode = await RunAsync(args, () => throw new InvalidOperationException("Cannot encrypt s3cr3t-value of db.password for acme.billing."));

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);

        var sentryEvent = _transport.Events.Single();
        Formatted(sentryEvent).ShouldStartWith("Unhandled exception at sform storyteller kv set --");
        Formatted(sentryEvent).ShouldContain("--secret");
        Formatted(sentryEvent).ShouldContain("--projectKey");
        ExceptionValue(sentryEvent).ShouldBe("Cannot encrypt *** of *** for ***.");
        _transport.AllText.ShouldNotContain("s3cr3t");
        _transport.AllText.ShouldNotContain("db.password");
        _transport.AllText.ShouldNotContain("acme");

        _logger.AllText.ShouldNotContain("s3cr3t");
        _logger.AllText.ShouldNotContain("db.password");
        _logger.AllText.ShouldContain("Cannot encrypt *** of *** for acme.billing.");
        _logger.Entries.Single().Message.ShouldEndWith("; --projectKey acme.billing");
    }

    [Fact]
    public async Task AnUnknownCommand_IsReportedByThePathOnly()
    {
        string[] args = ["story", "kv", "sett", "db.password", "--pasword=hunter2"];

        var exitCode = await RunAsync(args);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);

        var sentryEvent = _transport.Events.Single();
        sentryEvent.GetProperty("level").GetString().ShouldBe("warning");
        Formatted(sentryEvent).ShouldBe("Unrecognized input at sform storyteller kv");
        sentryEvent.TryGetProperty("exception", out _).ShouldBeFalse();
        _transport.AllText.ShouldNotContain("sett");
        _transport.AllText.ShouldNotContain("hunter2");
        _transport.AllText.ShouldNotContain("pasword");
        _transport.AllText.ShouldNotContain("db.password");

        _logger.Entries.Single().Message.ShouldBe("Unrecognized input at sform storyteller kv");
        _logger.AllText.ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task AnInvalidValue_IsReportedWithoutTheMessage()
    {
        string[] args = ["story", "kv", "set", "db.password", "--expected-version", "s3cr3t"];

        var exitCode = await RunAsync(args);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Formatted(_transport.Events.Single()).ShouldBe("Invalid input at sform storyteller kv set --expected-version");
        _transport.AllText.ShouldNotContain("s3cr3t");
        _logger.AllText.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task ACrash_KeepsTheTypesAndTheStack_AndLosesThePath()
    {
        const string path = @"C:\Users\ada\secret-plans\out.zip";
        string[] args = ["story", "export", "-o", path];

        var exitCode = await RunAsync(args, () => throw new InvalidOperationException(
            "The export could not be saved.",
            ErrorReporterTests.Thrown(() => new FileNotFoundException($"Could not find file '{path}'.", path))));

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);

        var sentryEvent = _transport.Events.Single();
        sentryEvent.GetProperty("level").GetString().ShouldBe("error");
        Formatted(sentryEvent).ShouldBe("Unhandled exception at sform storyteller export --output");
        var values = sentryEvent.GetProperty("exception").GetProperty("values").EnumerateArray().ToList();
        values.Select(value => value.GetProperty("type").GetString()).ShouldBe(["System.IO.FileNotFoundException", "System.InvalidOperationException"]);
        values.Select(value => value.GetProperty("value").GetString()).ShouldBe(["Could not find file '***'.", "The export could not be saved."]);
        values.ShouldAllBe(value => value.GetProperty("stacktrace").GetProperty("frames").GetArrayLength() > 0);
        _transport.AllText.ShouldNotContain("secret-plans");

        var logged = _logger.Entries.Single().Exception.ShouldNotBeNull().ToString();
        logged.ShouldContain("System.IO.FileNotFoundException: Could not find file '***'.");
        logged.ShouldContain(nameof(ErrorReporterTests.Thrown));
        _logger.AllText.ShouldNotContain("secret-plans");
    }

    [Fact]
    public async Task AFailureAfterTheTelemetryOptOut_GoesToTheLogOnly()
    {
        var reporter = ErrorReporter.Create(_logger, ErrorReporterTests.Settings(isOptedOut: true), options => options.Transport = _transport);

        var exitCode = await Program.RunAsync(() => throw new InvalidOperationException("boom"), ["story"], _run, reporter);

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _transport.Envelopes.ShouldBeEmpty();
        _logger.Entries.Single().Message.ShouldBe("Unhandled exception at unknown");
    }

    // The command of the run parses the arguments as Main does (the run recorded from the root's OnParsingComplete), then
    // fails as given.
    private Task<int> RunAsync(string[] args, Action? fail = null)
    {
        return Program.RunAsync(
            () =>
            {
                SformApplication.Create(_run).Parse(args);
                fail?.Invoke();
                return Task.FromResult(ExitCodes.SUCCESS);
            },
            args,
            _run,
            _reporter);
    }

    private static string? Formatted(System.Text.Json.JsonElement sentryEvent)
    {
        return sentryEvent.GetProperty("logentry").GetProperty("formatted").GetString();
    }

    private static string? ExceptionValue(System.Text.Json.JsonElement sentryEvent)
    {
        return sentryEvent.GetProperty("exception").GetProperty("values").EnumerateArray().Single().GetProperty("value").GetString();
    }
}
