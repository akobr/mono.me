using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Cli.ErrorReporting;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Cli.UnitTests.Telemetry;
using Microsoft.Extensions.Logging;
using Sentry;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// When a report goes to Sentry, with which options, and what the event and the log line hold. Every report has its own
// SentryClient, so these tests share no state.
public class ErrorReporterTests
{
    internal const string Dsn = "https://public@o1.ingest.de.sentry.io/1";
    internal const string InstallationId = "3f1c2a9e5b7d4c119e0a2d4f6b8c1a07";

    [Fact]
    public void WithoutADsn_NothingGoesToSentry()
    {
        ErrorReporter.Create(null, Settings(dsn: " ")).IsSentryEnabled.ShouldBeFalse();
        ErrorReporter.Create(null, Settings(dsn: null)).IsSentryEnabled.ShouldBeFalse();
        ErrorReporter.None.IsSentryEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task WithTheTelemetryOptOut_NothingGoesToSentry_AndTheLogStillGetsTheReport()
    {
        var transport = new RecordingTransport();
        var logger = new RecordingLogger();
        var reporter = ErrorReporter.Create(logger, Settings(isOptedOut: true), options => options.Transport = transport);

        await reporter.ReportAsync(LogLevel.Error, "Unhandled exception", Run("story", "kv", "get", "key"), Thrown(() => new InvalidOperationException("boom")));

        reporter.IsSentryEnabled.ShouldBeFalse();
        transport.Envelopes.ShouldBeEmpty();
        logger.Entries.Single().Message.ShouldBe("Unhandled exception at sform storyteller kv get");
    }

    [Fact]
    public async Task ADsnTheClientRefuses_ThrowsNothing_AndTheLogStillGetsTheReport()
    {
        var logger = new RecordingLogger();
        var reporter = ErrorReporter.Create(logger, Settings(dsn: "not a dsn"));

        await Should.NotThrowAsync(() => reporter.ReportAsync(LogLevel.Warning, "Unrecognized input", Run("story")));

        logger.Entries.Single().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void TheOptions_KeepThePersonTheMachineAndEverythingButTheReportOut()
    {
        var options = ErrorReporter.CreateOptions(Settings(), []);

        options.Dsn.ShouldBe(Dsn);
        options.SendDefaultPii.ShouldBeFalse();
        options.IsEnvironmentUser.ShouldBeFalse();
        options.ServerName.ShouldBeNull();
        options.DisableFileWrite.ShouldBeTrue();
        options.AttachStacktrace.ShouldBeFalse();
        options.MaxBreadcrumbs.ShouldBe(0);
        options.AutoSessionTracking.ShouldBeFalse();
        options.TracesSampleRate.ShouldBeNull();
        options.EnableLogs.ShouldBeFalse();
        options.CaptureFailedRequests.ShouldBeFalse();
        options.Release.ShouldBe("42.Platform.Cli@1.2.3");
        options.Environment.ShouldBe("production");
        options.DefaultTags[ReportScrubber.InstallationTag].ShouldBe(InstallationId);
    }

    [Fact]
    public async Task AnEvent_NamesTheCommandAndTheOptions_AndCarriesNoValue()
    {
        var transport = new RecordingTransport();
        var reporter = ErrorReporter.Create(null, Settings(), options => options.Transport = transport);
        var run = Run("story", "kv", "get", "db.password", "-p", "acme.billing");

        await reporter.ReportAsync(LogLevel.Warning, "The API refused the request with 403", run);

        var sentryEvent = transport.Events.Single();
        sentryEvent.GetProperty("level").GetString().ShouldBe("warning");
        sentryEvent.GetProperty("logentry").GetProperty("message").GetString().ShouldBe("The API refused the request with 403 at {command}");
        sentryEvent.GetProperty("logentry").GetProperty("formatted").GetString().ShouldBe("The API refused the request with 403 at sform storyteller kv get --projectKey");
        Tags(sentryEvent).ShouldBe([(ReportScrubber.CommandTag, "sform storyteller kv get"), (ReportScrubber.InstallationTag, InstallationId)], ignoreOrder: true);
        var command = sentryEvent.GetProperty("contexts").GetProperty(ReportScrubber.CommandContext);
        command.GetProperty("name").GetString().ShouldBe("sform storyteller kv get");
        command.GetProperty("options").EnumerateArray().Select(option => option.GetString()).ShouldBe(["--projectKey"]);
        sentryEvent.GetProperty("user").GetProperty("id").GetString().ShouldBe(InstallationId);
        sentryEvent.GetProperty("environment").GetString().ShouldBe("production");
        sentryEvent.GetProperty("release").GetString().ShouldBe("42.Platform.Cli@1.2.3");
        sentryEvent.TryGetProperty("server_name", out _).ShouldBeFalse();
        sentryEvent.TryGetProperty("breadcrumbs", out _).ShouldBeFalse();
        sentryEvent.TryGetProperty("threads", out _).ShouldBeFalse();
        transport.AllText.ShouldNotContain("db.password");
        transport.AllText.ShouldNotContain("acme");
    }

    [Fact]
    public async Task AnException_KeepsItsTypesAndStack_AndLosesTheValuesTheContextAndThePaths()
    {
        var transport = new RecordingTransport();
        var logger = new RecordingLogger();
        var reporter = ErrorReporter.Create(logger, Settings(contextValues: ["northwind.invoicing"]), options => options.Transport = transport);
        var run = Run("story", "export", "-o", @"C:\Users\ada\plans\out.zip", "-p", "acme.billing");
        var exception = Thrown(() => new InvalidOperationException(
            "The export of acme.billing for northwind.invoicing failed.",
            Thrown(() => new FileNotFoundException(@"Could not find file 'C:\Users\ada\plans\out.zip'.", @"C:\Users\ada\plans\out.zip"))));
        exception.Data["key"] = "s3cr3t-data";

        await reporter.ReportAsync(LogLevel.Error, "Unhandled exception", run, exception);

        var values = transport.Events.Single().GetProperty("exception").GetProperty("values").EnumerateArray().ToList();
        values.Select(value => value.GetProperty("type").GetString()).ShouldBe(["System.IO.FileNotFoundException", "System.InvalidOperationException"]);
        values.Select(value => value.GetProperty("value").GetString()).ShouldBe(["Could not find file '***'.", "The export of *** for *** failed."]);
        values.ShouldAllBe(value => value.GetProperty("stacktrace").GetProperty("frames").GetArrayLength() > 0);
        transport.AllText.ShouldNotContain("plans");
        transport.AllText.ShouldNotContain("acme");
        transport.AllText.ShouldNotContain("northwind");
        transport.AllText.ShouldNotContain("s3cr3t-data");
        transport.AllText.ShouldNotContain(@"\\Users\\ada");

        // The local log keeps the project, the stack and the types, not the path.
        var entry = logger.Entries.Single();
        entry.Message.ShouldStartWith("Unhandled exception at sform storyteller export --");
        entry.Message.ShouldContain(" --output");
        entry.Message.ShouldEndWith(" --projectKey; --projectKey acme.billing");
        var logged = entry.Exception.ShouldNotBeNull().ToString();
        logged.ShouldStartWith("System.InvalidOperationException: The export of acme.billing for northwind.invoicing failed. ---> System.IO.FileNotFoundException: Could not find file '***'.");
        logged.ShouldContain(nameof(Thrown));
        logged.ShouldNotContain("plans");
    }

    [Fact]
    public void TheEvent_IsBuiltFromTheUsage()
    {
        var sentryEvent = ErrorReporter.CreateEvent(LogLevel.Error, "Unhandled exception", new CommandUsage("sform storyteller kv set", ["--secret"]), null);

        sentryEvent.Level.ShouldBe(SentryLevel.Error);
        sentryEvent.Message!.Formatted.ShouldBe("Unhandled exception at sform storyteller kv set --secret");
        sentryEvent.Tags[ReportScrubber.CommandTag].ShouldBe("sform storyteller kv set");
    }

    internal static ErrorReportingSettings Settings(string? dsn = Dsn, bool isOptedOut = false, string?[]? contextValues = null)
    {
        return new ErrorReportingSettings(dsn, isOptedOut, InstallationId, "production", "42.Platform.Cli@1.2.3", contextValues ?? []);
    }

    internal static CommandRun Run(params string[] args)
    {
        var run = new CommandRun();
        run.RecordArguments(args);
        SformApplication.Create(run).Parse(args);
        return run;
    }

    // An exception with a stack, as one that reaches Main.
    internal static Exception Thrown(Func<Exception> create)
    {
        try
        {
            throw create();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static (string, string?)[] Tags(JsonElement sentryEvent)
    {
        return sentryEvent.GetProperty("tags").EnumerateObject().Select(tag => (tag.Name, tag.Value.GetString())).ToArray();
    }
}
