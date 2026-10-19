using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Api;

namespace _42.Platform.Cli.Telemetry;

// Sends the cli_command_executed event of a run as a personless event of the anonymous installation id.
// The properties are an allowlist: never an argument, an option value, an exception message, a name of an
// organization, project, view or key, a URL, a path, a user or a token.
internal sealed class PostHogCommandTelemetry : ICommandTelemetry
{
    public const string EVENT_NAME = "cli_command_executed";

    // FlushAsync takes no token and DisposeAsync flushes again under the same lock; the HttpClient timeout of
    // CommandTelemetry ends an abandoned request, these limits only end the wait of Main.
    internal static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(0.5);

    // Not the toolkit's version getter of sform --version: it reads the version of 42.CLI.Toolkit.
    private static readonly string CliVersion = ReadCliVersion();

    private readonly IPostHogClient _client;
    private readonly string _installationId;
    private readonly string _environmentName;
    private readonly bool _isContinuousIntegration;
    private readonly bool _isMachineSignIn;
    private readonly ILogger _logger;

    public PostHogCommandTelemetry(
        IPostHogClient client,
        string installationId,
        string environmentName,
        bool isContinuousIntegration,
        bool isMachineSignIn,
        ILogger logger)
    {
        _client = client;
        _installationId = installationId;
        _environmentName = environmentName;
        _isContinuousIntegration = isContinuousIntegration;
        _isMachineSignIn = isMachineSignIn;
        _logger = logger;
    }

    public string? InstallationId => _installationId;

    public async Task SendAsync(CommandRun run, int exitCode)
    {
        try
        {
            _client.Capture(_installationId, EVENT_NAME, GetProperties(run, exitCode), groups: null, flags: null);
            await _client.FlushAsync().WaitAsync(FlushTimeout);
        }
        catch (Exception exception)
        {
            // A TimeoutException, the TaskCanceledException of the HttpClient timeout, or anything else: the event is dropped.
            TryLog(logger => logger.LogInformation(exception, "The usage event was not sent."));
        }

        try
        {
            await _client.DisposeAsync().AsTask().WaitAsync(DisposeTimeout);
        }
        catch (Exception exception)
        {
            TryLog(logger => logger.LogInformation(exception, "The PostHog client did not stop in time."));
        }
    }

    internal Dictionary<string, object> GetProperties(CommandRun run, int exitCode)
    {
        var properties = new Dictionary<string, object>
        {
            ["command"] = run.Command ?? CommandPath.UNKNOWN,
            ["outcome"] = CommandOutcome.Of(run, exitCode),
            ["exit_code"] = exitCode,
            ["duration_ms"] = run.Stopwatch.ElapsedMilliseconds,
            ["cli_version"] = CliVersion,
            ["os"] = GetOperatingSystem(),
            ["os_architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["runtime"] = Environment.Version.ToString(),
            ["environment"] = _environmentName,
            ["ci"] = _isContinuousIntegration,
            ["machine_sign_in"] = _isMachineSignIn,
            [PostHogProperties.ProcessPersonProfile] = false,
            [PostHogProperties.GeoIpDisable] = true,
        };

        if (run.Exception is { } exception)
        {
            properties["exception"] = GetTypeName(exception.GetType());
        }

        if (run.Exception is AuthenticationException authenticationException)
        {
            properties["authentication_failure"] = authenticationException.Reason.ToString();
        }

        if (run.Exception is ApiException apiException)
        {
            properties["api_status"] = apiException.StatusCode;
        }

        return properties;
    }

    private static string ReadCliVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? CommandPath.UNKNOWN;
    }

    // The name without the arity suffix of a generic type: ApiException<ErrorResponse> is "ApiException", not "ApiException`1".
    private static string GetTypeName(Type type)
    {
        var name = type.Name;
        var aritySuffix = name.IndexOf('`');
        return aritySuffix < 0 ? name : name[..aritySuffix];
    }

    private static string GetOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return OperatingSystem.IsMacOS() ? "macos" : "other";
    }

    // The logger of Program outlives the host as Main's own log calls do; if it fails anyway, the run still does not.
    private void TryLog(Action<ILogger> log)
    {
        try
        {
            log(_logger);
        }
        catch (Exception)
        {
            // Nothing to report to.
        }
    }
}
