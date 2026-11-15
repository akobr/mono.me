using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Telemetry;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Extensibility;

namespace _42.Platform.Cli.ErrorReporting;

// The failures of a run that Main reports: to the local log, and to Sentry with a DSN and without the telemetry opt-out.
// Only this class sends to Sentry; no log entry does. What a report may carry:
// - Sentry: what happened, the command path and the names of the options, the type and the stack of the exception and
//   its message scrubbed of every value of the run, the context values and customer data (ReportScrubber.ScrubForSentry).
// - The local log: the same, the project and the view when the run was given them, and the whole exception with its
//   messages scrubbed of the other values and of secrets (RedactedException).
// Each report has its own SentryClient, no SentrySdk.Init: no static hub, no integration, and no window in which
// another log entry or an unobserved exception becomes an event. Never throws.
internal sealed class ErrorReporter
{
    public static readonly ErrorReporter None = new(null, null, []);

    private readonly ILogger? _logger;
    private readonly ErrorReportingSettings? _sentry;
    private readonly IReadOnlyList<string> _contextValues;
    private readonly Action<SentryOptions>? _configure;

    private ErrorReporter(ILogger? logger, ErrorReportingSettings? sentry, IReadOnlyList<string> contextValues, Action<SentryOptions>? configure = null)
    {
        _logger = logger;
        _sentry = sentry;
        _contextValues = contextValues;
        _configure = configure;
    }

    // Whether a report goes to Sentry.
    public bool IsSentryEnabled => _sentry is not null;

    // configure: the tests' transport.
    public static ErrorReporter Create(ILogger? logger, ErrorReportingSettings settings, Action<SentryOptions>? configure = null)
    {
        var contextValues = settings.ContextValues
            .Select(value => value?.Trim())
            .OfType<string>()
            .Where(value => value.Length > 0)
            .SelectMany(CommandLineValues.WithParts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        var sentry = string.IsNullOrWhiteSpace(settings.Dsn) || settings.IsOptedOut ? null : settings;
        return new ErrorReporter(logger, sentry, contextValues, configure);
    }

    // what: a fixed text, with at most a status code or the name of an enum formatted in; never a value.
    public async Task ReportAsync(LogLevel level, string what, CommandRun run, Exception? exception = null)
    {
        var usage = run.Usage ?? (run.Command is { } command ? new CommandUsage(command, []) : CommandUsage.Unknown);
        var values = run.Values;

        WriteLog(level, what, usage, values, exception);

        if (_sentry is not null)
        {
            // The client flushes when it is disposed, for up to its ShutdownTimeout.
            await Task.Run(() => Send(_sentry, level, what, usage, values, exception));
        }
    }

    internal static SentryOptions CreateOptions(ErrorReportingSettings settings, IReadOnlyCollection<string> values)
    {
        var options = new SentryOptions
        {
            Dsn = settings.Dsn,
            Release = settings.Release,
            Environment = settings.Environment,

            // Nothing about the person or the machine, and no file outside SFORM_HOME (the SDK's installation id).
            SendDefaultPii = false,
            IsEnvironmentUser = false,
            ServerName = null,
            DisableFileWrite = true,
            CacheDirectoryPath = null,

            // The report, no more: no stack of the reporting thread, no breadcrumb, no session, no tracing, no logs.
            AttachStacktrace = false,
            MaxBreadcrumbs = 0,
            AutoSessionTracking = false,
            TracesSampleRate = null,
            EnableLogs = false,
            CaptureFailedRequests = false,
        };

        options.SetBeforeBreadcrumb((_, _) => null);
        options.SetBeforeSendTransaction((_, _) => null);
        options.SetBeforeSend((sentryEvent, _) => ReportScrubber.ScrubEvent(sentryEvent, values, settings.InstallationId));

        if (!string.IsNullOrEmpty(settings.InstallationId))
        {
            options.DefaultTags[ReportScrubber.InstallationTag] = settings.InstallationId;
        }

        return options;
    }

    internal static SentryEvent CreateEvent(LogLevel level, string what, CommandUsage usage, Exception? exception)
    {
        var sentryEvent = new SentryEvent(exception)
        {
            Level = level switch
            {
                LogLevel.Critical => SentryLevel.Fatal,
                LogLevel.Error => SentryLevel.Error,
                LogLevel.Warning => SentryLevel.Warning,
                LogLevel.Information => SentryLevel.Info,
                _ => SentryLevel.Debug,
            },
            Message = new SentryMessage
            {
                Message = $"{what} at {{command}}",
                Formatted = $"{what} at {usage}",
            },
        };

        sentryEvent.SetTag(ReportScrubber.CommandTag, usage.Command);
        sentryEvent.Contexts[ReportScrubber.CommandContext] = new Dictionary<string, object>
        {
            ["name"] = usage.Command,
            ["options"] = usage.Options.ToArray(),
        };

        return sentryEvent;
    }

    private void WriteLog(LogLevel level, string what, CommandUsage usage, CommandLineValues values, Exception? exception)
    {
        if (_logger is null)
        {
            return;
        }

        try
        {
            var localValues = values.LocalValues.Count == 0
                ? string.Empty
                : $"; {string.Join(' ', values.LocalValues.Select(pair => $"{pair.Key} {ReportScrubber.ScrubSecrets(pair.Value, values.Hidden)}"))}";

            _logger.Log(level, Redact(exception, values.Hidden), "{what} at {command}{values}", what, usage.ToString(), localValues);
        }
        catch (Exception)
        {
            // A report never stops the run, and never falls back to the raw texts.
        }
    }

    private static Exception? Redact(Exception? exception, IReadOnlyList<string> hidden)
    {
        if (exception is null)
        {
            return null;
        }

        try
        {
            return new RedactedException(exception, hidden);
        }
        catch (Exception)
        {
            // A scrubbing rule timed out: the type, never the raw message.
            return new ReportedException(exception.GetType());
        }
    }

    private void Send(ErrorReportingSettings settings, LogLevel level, string what, CommandUsage usage, CommandLineValues values, Exception? exception)
    {
        try
        {
            var scrubbed = values.All.Concat(_contextValues).Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            var options = CreateOptions(settings, scrubbed);
            _configure?.Invoke(options);

            using var client = new SentryClient(options);
            client.CaptureEvent(CreateEvent(level, what, usage, exception));
        }
        catch (Exception)
        {
            // A DSN the client refuses, or a transport that fails: the report stays in the local log.
        }
    }

    // Stands in for an exception whose message could not be scrubbed: its type only.
    private sealed class ReportedException(Type type) : Exception($"An exception of type {type.FullName} (its message is left out).");
}
