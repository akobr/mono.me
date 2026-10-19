using System;
using System.IO.Abstractions;
using System.Net.Http;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PostHog;

namespace _42.Platform.Cli.Telemetry;

// Builds the telemetry of a run in InitializeHost, outside the container: the host disposes its singletons, its
// IHttpClientFactory and its ILoggerFactory before Main sends the event, so the PostHog client gets its own
// HttpClient and no logger factory. Never throws; anything unexpected turns telemetry off for the run.
internal static class CommandTelemetry
{
    // Bounds every request of the client, and so the abandoned flush that DisposeAsync waits for.
    internal static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(1.5);

    internal static readonly string[] Notice =
    [
        "sform sends anonymous usage data (the command, its outcome and duration) to PostHog.",
        "Set DO_NOT_TRACK=1 to turn it off: https://42for.net/cli/sform#telemetry",
    ];

    public static ICommandTelemetry Create(
        TelemetryOptions? options,
        IFileSystem fileSystem,
        IHostEnvironment environment,
        MachineSignInSettings? machineSignIn,
        ILogger logger)
    {
        try
        {
            return Create(
                TelemetrySettings.Resolve(options ?? new TelemetryOptions(), System.Environment.GetEnvironmentVariable),
                new InstallationIdStore(fileSystem),
                environment.EnvironmentName,
                machineSignIn?.IsConfigured == true,
                logger,
                new TimeoutHttpClientFactory(HttpTimeout),
                WriteNotice);
        }
        catch (Exception)
        {
            return NoCommandTelemetry.Instance;
        }
    }

    internal static ICommandTelemetry Create(
        TelemetrySettings settings,
        InstallationIdStore store,
        string environmentName,
        bool isMachineSignIn,
        ILogger logger,
        IHttpClientFactory httpClientFactory,
        Action<string[]> writeNotice,
        bool enableCompression = true)
    {
        try
        {
            if (!settings.IsEnabled
                || settings.Host is null
                || !store.TryGetOrCreate(out var installationId, out var created))
            {
                return NoCommandTelemetry.Instance;
            }

            var client = new PostHogClient(
                new PostHogOptions
                {
                    ProjectToken = settings.ProjectToken,
                    HostUrl = settings.Host,
                    IsServer = false,
                    MaxRetries = 0,
                    EnableCompression = enableCompression,
                },
                httpClientFactory: httpClientFactory,
                loggerFactory: null);

            if (created)
            {
                writeNotice(Notice);
            }

            return new PostHogCommandTelemetry(
                client,
                installationId,
                environmentName,
                settings.IsContinuousIntegration,
                isMachineSignIn,
                logger);
        }
        catch (Exception)
        {
            return NoCommandTelemetry.Instance;
        }
    }

    // On standard error: sform writes command output, an export for example, to standard output.
    private static void WriteNotice(string[] lines)
    {
        System.Console.ForegroundColor = ConsoleColor.DarkGray;

        try
        {
            foreach (var line in lines)
            {
                System.Console.Error.WriteLine(line);
            }
        }
        finally
        {
            System.Console.ResetColor();
        }
    }

    private sealed class TimeoutHttpClientFactory : IHttpClientFactory
    {
        private readonly TimeSpan _timeout;
        private HttpClient? _client;

        public TimeoutHttpClientFactory(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public HttpClient CreateClient(string name)
        {
            return _client ??= new HttpClient { Timeout = _timeout };
        }
    }
}
