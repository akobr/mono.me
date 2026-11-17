using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Telemetry;
using Microsoft.Extensions.Logging;
using Sentry;

namespace _42.Platform.Cli.ErrorReporting;

// The failures of a run that Main reports: to the local log, and to Sentry with a DSN and without the telemetry opt-out.
// Only this class sends to Sentry; no log entry does. What a report may carry:
// - Sentry: what happened, the command path and the names of the options, the type and the stack of the exception and
//   its first line scrubbed of customer data and of every value of the run and of the context
//   (ReportScrubber.ScrubForSentry).
// - The local log: the same, the project and the view when the run was given them, and the whole exception with its
//   messages scrubbed of the other values and of secrets (RedactedException).
// A value that names a file is also hidden in its full form, as a message of System.IO names it.
// Each report has its own SentryClient, no SentrySdk.Init: no static hub, no integration, and no window in which
// another log entry or an unobserved exception becomes an event. Never throws.
internal sealed class ErrorReporter
{
    public static readonly ErrorReporter None = new(null, null, new ErrorReportingSettings(), null, new FileSystem());

    private readonly ILogger? _logger;
    private readonly ErrorReportingSettings? _sentry;
    private readonly ErrorReportingSettings _settings;
    private readonly Action<SentryOptions>? _configure;
    private readonly IFileSystem _fileSystem;

    private ErrorReporter(ILogger? logger, ErrorReportingSettings? sentry, ErrorReportingSettings settings, Action<SentryOptions>? configure, IFileSystem fileSystem)
    {
        _logger = logger;
        _sentry = sentry;
        _settings = settings;
        _configure = configure;
        _fileSystem = fileSystem;
    }

    // Whether a report goes to Sentry.
    public bool IsSentryEnabled => _sentry is not null;

    // configure: the tests' transport. fileSystem: only its paths, for the full form of a value that names a file.
    public static ErrorReporter Create(ILogger? logger, ErrorReportingSettings settings, Action<SentryOptions>? configure = null, IFileSystem? fileSystem = null)
    {
        var sentry = string.IsNullOrWhiteSpace(settings.Dsn) || settings.IsOptedOut ? null : settings;
        return new ErrorReporter(logger, sentry, settings, configure, fileSystem ?? new FileSystem());
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
        options.SetBeforeSend((sentryEvent, _) => ScrubOrDrop(sentryEvent, values, settings.InstallationId));

        if (!string.IsNullOrEmpty(settings.InstallationId))
        {
            options.DefaultTags[ReportScrubber.InstallationTag] = settings.InstallationId;
        }

        return options;
    }

    // An event that cannot be scrubbed (a rule that timed out) is dropped here, not left to how the SDK treats a callback
    // that throws.
    internal static SentryEvent? ScrubOrDrop(SentryEvent sentryEvent, IReadOnlyCollection<string> values, string? installationId)
    {
        try
        {
            return ReportScrubber.ScrubEvent(sentryEvent, values, installationId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static SentryEvent CreateEvent(LogLevel level, string what, CommandUsage usage, Exception? exception)
    {
        RemoveSentryData(exception);

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

    // The SDK turns an Exception.Data key "sentry:…" into an extra, a tag or a context named by the key, which BeforeSend
    // cannot rename: the keys go before the capture, from the whole chain. The exception is not used after its report.
    private static void RemoveSentryData(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            foreach (var key in exception.Data.Keys.OfType<string>().Where(key => key.StartsWith("sentry:", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                exception.Data.Remove(key);
            }
        }
        catch (Exception)
        {
            // A read-only Data: BeforeSend still masks the values.
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                RemoveSentryData(inner);
            }
        }
        else
        {
            RemoveSentryData(exception.InnerException);
        }
    }

    private static IEnumerable<string?> ReportTimeValues(ErrorReportingSettings settings)
    {
        try
        {
            return settings.ReportTimeValues?.Invoke().ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void WriteLog(LogLevel level, string what, CommandUsage usage, CommandLineValues values, Exception? exception)
    {
        if (_logger is null)
        {
            return;
        }

        try
        {
            var hidden = Expand(values.Hidden.Concat(_settings.SecretValues));
            var localValues = values.LocalValues.Count == 0
                ? string.Empty
                : $"; {string.Join(' ', values.LocalValues.Select(pair => $"{pair.Key} {ReportScrubber.ScrubSecrets(pair.Value, hidden)}"))}";

            _logger.Log(level, exception is null ? null : RedactedException.Of(exception, hidden), "{what} at {command}{values}", what, usage.ToString(), localValues);
        }
        catch (Exception)
        {
            // A report never stops the run, and never falls back to the raw texts.
        }
    }

    private void Send(ErrorReportingSettings settings, LogLevel level, string what, CommandUsage usage, CommandLineValues values, Exception? exception)
    {
        try
        {
            var scrubbed = Expand(values.All.Concat(settings.ContextValues).Concat(settings.SecretValues).Concat(ReportTimeValues(settings)));
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

    // Each value with its parts, and in its full form as a path and with the other separator: ../ops/db.env typed on Windows
    // is C:\work\ops\db.env in the message of System.IO.
    private IReadOnlyList<string> Expand(IEnumerable<string?> values)
    {
        var expanded = new List<string>();

        foreach (var value in values.Select(value => value?.Trim()).OfType<string>().Where(value => value.Length > 0))
        {
            expanded.AddRange(CommandLineValues.WithParts(value));

            if (value.IndexOfAny(['/', '\\']) >= 0)
            {
                expanded.Add(value.Replace('/', '\\'));
                expanded.Add(value.Replace('\\', '/'));
            }

            // A bare file name too (-o out.zip): System.IO names it by its full path.
            try
            {
                expanded.Add(_fileSystem.Path.GetFullPath(value));
            }
            catch (Exception)
            {
                // Not a path of this machine.
            }
        }

        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }
}
