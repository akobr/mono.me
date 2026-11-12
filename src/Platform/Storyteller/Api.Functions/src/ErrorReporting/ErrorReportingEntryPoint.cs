using System.Globalization;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sentry;
using Sentry.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The API's errors to Sentry, next to Application Insights. Off without a valid Sentry:Dsn (local runs, Aspire, tests):
// then nothing of Sentry is registered. The options that decide what an event may carry are set after every other
// configuration of the SDK, so no setting can weaken them.
public static class ErrorReportingEntryPoint
{
    public static bool IsErrorReportingEnabled(this IConfiguration configuration)
    {
        return IsValidDsn(configuration.GetSection(ErrorReportingOptions.SectionName)[nameof(ErrorReportingOptions.Dsn)]);
    }

    public static IServiceCollection AddErrorReporting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        return services.AddErrorReporting(configuration, environment, configure: null);
    }

    // configure: the tests' transport and integrations, applied last; time: the throttle's clock.
    // An optional diagnostics setting must not stop the API: a malformed DSN turns Sentry off (the SDK would throw while
    // the host is built), a malformed volume setting keeps its default; each says so once at start, never with its value.
    internal static IServiceCollection AddErrorReporting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<SentryLoggingOptions>? configure,
        TimeProvider? time = null)
    {
        var section = configuration.GetSection(ErrorReportingOptions.SectionName);
        var rawDsn = section[nameof(ErrorReportingOptions.Dsn)];

        if (string.IsNullOrWhiteSpace(rawDsn))
        {
            return services;
        }

        if (!IsValidDsn(rawDsn))
        {
            services.AddSingleton<IHostedService>(provider => new ErrorReportingWarnings(
                provider.GetRequiredService<ILogger<ErrorReportingWarnings>>(),
                [$"{ErrorReportingOptions.SectionName}:{nameof(ErrorReportingOptions.Dsn)} is not a valid DSN; errors are not reported to Sentry."]));
            return services;
        }

        var (options, invalid) = ReadOptions(section);

        if (invalid.Count > 0)
        {
            services.AddSingleton<IHostedService>(provider => new ErrorReportingWarnings(
                provider.GetRequiredService<ILogger<ErrorReportingWarnings>>(),
                invalid.Select(key => $"{ErrorReportingOptions.SectionName}:{key} is not valid; its default is used.").ToList()));
        }

        var dsn = rawDsn.Trim();
        var environmentName = environment.EnvironmentName.ToLowerInvariant();
        var release = GetRelease();
        var throttle = new SentryEventThrottle(options, time ?? TimeProvider.System);

        services.AddLogging(logging => logging.AddSentry());
        services.PostConfigure<SentryLoggingOptions>(sentry =>
        {
            sentry.Dsn = dsn;
            sentry.Environment = environmentName; // "production", like the admin UI
            sentry.Release = release;
            sentry.InitializeSdk = true;

            sentry.MinimumEventLevel = LogLevel.Error;
            sentry.MinimumBreadcrumbLevel = LogLevel.Information;
            sentry.MaxBreadcrumbs = 30;
            sentry.AddLogEntryFilter(IsFilteredOut);

            // Nothing of a person, a request or the machine.
            sentry.SendDefaultPii = false;
            sentry.IsEnvironmentUser = false;
            sentry.ServerName = null;

            // No handler in the HttpClients: no breadcrumbs with outgoing URLs, no events of outgoing 5xx, no trace headers to
            // WorkOS, a federated issuer or Keycloak.
            sentry.CaptureFailedRequests = false;
            sentry.DisableSentryHttpMessageHandler = true;

            // Errors only: no tracing, no sessions, no Sentry logs.
            sentry.TracesSampleRate = null;
            sentry.TracesSampler = null;
            sentry.AutoSessionTracking = false;
            sentry.EnableLogs = false;

            // Breadcrumbs only inside an invocation: outside it they would gather in the shared root scope (hosted services,
            // refresh callbacks, other organizations) and ride along with every later event.
            sentry.SetBeforeBreadcrumb((breadcrumb, _) => SentryScopeMiddleware.IsInInvocation
                ? SentryScrubber.ScrubBreadcrumb(breadcrumb)
                : null);
            sentry.SetBeforeSend((sentryEvent, _) =>
            {
                SentryScrubber.CompleteFunction(sentryEvent);

                if (!throttle.ShouldSend(sentryEvent))
                {
                    return null;
                }

                SentryScrubber.ScrubEvent(sentryEvent);
                return sentryEvent;
            });
            sentry.SetBeforeSendTransaction((_, _) => null);

            configure?.Invoke(sentry);
        });

        return services;
    }

    // The rule of Sentry's Dsn.Parse (6.12.1), so a DSN accepted here never stops the host: an absolute http(s) URI, the
    // public key is the user info before ':', the project id everything after the last '/' of the absolute URI (a
    // trailing '/' or "#/" leaves it empty).
    internal static bool IsValidDsn(string? dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn)
            || !Uri.TryCreate(dsn.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var publicKey = uri.UserInfo.Split(':')[0];
        var absoluteUri = uri.AbsoluteUri;
        var projectId = absoluteUri[(absoluteUri.LastIndexOf('/') + 1)..];
        return !string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(projectId);
    }

    // The volume settings, each parsed on its own: a missing one keeps its default, a malformed or negative one too, and
    // its key (never its value) is reported.
    internal static (ErrorReportingOptions Options, IReadOnlyList<string> Invalid) ReadOptions(IConfigurationSection section)
    {
        var options = new ErrorReportingOptions { Dsn = section[nameof(ErrorReportingOptions.Dsn)]?.Trim() };
        var invalid = new List<string>();

        if (Read<TimeSpan>(nameof(ErrorReportingOptions.RepeatInterval), value =>
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var interval) && interval >= TimeSpan.Zero ? interval : null) is { } repeat)
        {
            options.RepeatInterval = repeat;
        }

        if (Read<int>(nameof(ErrorReportingOptions.MaxEventsPerHour), ParseCount) is { } perHour)
        {
            options.MaxEventsPerHour = perHour;
        }

        if (Read<int>(nameof(ErrorReportingOptions.MaxEventsPerDay), ParseCount) is { } perDay)
        {
            options.MaxEventsPerDay = perDay;
        }

        return (options, invalid);

        T? Read<T>(string key, Func<string, T?> parse)
            where T : struct
        {
            var value = section[key];

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parsed = parse(value.Trim());

            if (parsed is null)
            {
                invalid.Add(key);
            }

            return parsed;
        }

        static int? ParseCount(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0 ? count : null;
        }
    }

    // 42.Platform.Storyteller.Api.Functions@0.10.33-alpha+22da6eab60: the version and the commit of the build.
    internal static string GetRelease()
    {
        var assembly = typeof(ErrorReportingEntryPoint).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        return $"{assembly.GetName().Name}@{version}";
    }

    // True drops the entry. The Information of a library quotes URLs (request starting, outgoing calls), so only the
    // platform's own categories leave breadcrumbs below Warning. A cancellation that reaches the worker is not a defect:
    // ExceptionHandlingMiddleware logged it at Warning, or the caller went away while a result was written.
    internal static bool IsFilteredOut(string category, LogLevel level, EventId eventId, Exception? exception)
    {
        if (level < LogLevel.Warning && !category.StartsWith("_42.", StringComparison.Ordinal))
        {
            return true;
        }

        return exception is OperationCanceledException
            && category.StartsWith("Microsoft.Azure.Functions.Worker", StringComparison.Ordinal);
    }
}
