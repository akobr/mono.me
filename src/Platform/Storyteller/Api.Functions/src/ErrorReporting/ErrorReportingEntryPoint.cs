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

    // configure: the tests' transport and integrations, applied last.
    internal static IServiceCollection AddErrorReporting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<SentryLoggingOptions>? configure)
    {
        var options = configuration.GetSection(ErrorReportingOptions.SectionName).Get<ErrorReportingOptions>() ?? new ErrorReportingOptions();

        if (string.IsNullOrWhiteSpace(options.Dsn))
        {
            return services;
        }

        if (!IsValidDsn(options.Dsn))
        {
            // An optional diagnostics setting must not stop the API; the SDK would throw while the host is built.
            services.AddHostedService<InvalidDsnWarning>();
            return services;
        }

        var dsn = options.Dsn.Trim();
        var environmentName = environment.EnvironmentName.ToLowerInvariant();
        var release = GetRelease();
        var throttle = new SentryEventThrottle(options, TimeProvider.System);

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

            sentry.SetBeforeBreadcrumb((breadcrumb, _) => SentryScrubber.ScrubBreadcrumb(breadcrumb));
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

    // The shape Sentry parses: an absolute http(s) URI with a public key and a project id as its last path segment.
    internal static bool IsValidDsn(string? dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn)
            || !Uri.TryCreate(dsn.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var publicKey = uri.UserInfo.Split(':')[0];
        var path = uri.AbsolutePath.TrimEnd('/');
        var projectId = path[(path.LastIndexOf('/') + 1)..];
        return publicKey.Length > 0 && projectId.Length > 0;
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
