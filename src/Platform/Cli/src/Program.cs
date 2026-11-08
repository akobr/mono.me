using System;
using System.Drawing;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;

using Console = Colorful.Console;

namespace _42.Platform.Cli;

public class Program
{
    private const bool IsSourceBuild =
#if DEBUG
        true;
#else
        false;
#endif

    // What the usage event reports; the command line application itself is disposed with the host.
    private static readonly CommandRun CurrentRun = new();

    private static IHost? _host;
    private static ILogger<Program>? _logger;
    private static LoggingOptions? _loggingOptions;

    // Captured before the run: the host is disposed before a command's exception reaches Main.
    private static MachineSignInSettings? _machineSignIn;

    // The API address, for the message of a service that cannot be reached.
    private static string? _baseUrl;

    // Resolved before the host, from SFORM_HOME.
    private static CliPaths? _paths;

    // Built outside the container for the same reason.
    private static ICommandTelemetry _telemetry = NoCommandTelemetry.Instance;

    public static IHost Host => _host ?? throw new InvalidOperationException("The host app is not initialized.");

    public static async Task<int> Main(string[] args)
    {
        CurrentRun.Stopwatch.Start();

        if (ResolvePaths(System.Environment.GetEnvironmentVariable) is not { } paths)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        _paths = paths;

        if (!InitializeHost(args))
        {
            return ExitCodes.ERROR_CRASH;
        }

        System.Console.CancelKeyPress += OnCancelKeyPress;
        var exitCode = await RunAsync(args);
        await _telemetry.SendAsync(CurrentRun, exitCode);
        return exitCode;
    }

    // An invalid SFORM_HOME (relative, or a file) ends the run before anything is read or written: falling back to the
    // default folders would mix the state SFORM_HOME is meant to keep apart.
    internal static CliPaths? ResolvePaths(Func<string, string?> getVariable, IFileSystem? fileSystem = null, TextWriter? error = null)
    {
        try
        {
            return CliPaths.FromEnvironment(fileSystem ?? new FileSystem(), getVariable);
        }
        catch (WrongInputException exception)
        {
            (error ?? System.Console.Error).WriteLine($"! {exception.Message}");
            return null;
        }
    }

    // A release build always runs as Production: an installed tool never loads the CLI's user secrets, even where a
    // developer's shell sets DOTNET_ENVIRONMENT=Development. A build from source (Debug) runs as Development unless
    // DOTNET_ENVIRONMENT names another environment, so a program that runs it (the automation tests) can turn the user
    // secrets of the machine off with DOTNET_ENVIRONMENT=Production.
    internal static string ResolveEnvironmentName(Func<string, string?> getVariable, bool isSourceBuild = IsSourceBuild)
    {
        if (!isSourceBuild)
        {
            return Environments.Production;
        }

        var configured = getVariable(SformVariables.DOTNET_ENVIRONMENT);
        return string.IsNullOrWhiteSpace(configured) ? Environments.Development : configured.Trim();
    }

    // The run, and what its failures print and end with.
    internal static async Task<int> RunAsync(Func<Task<int>> run, string[] args)
    {
        try
        {
            return await run();
        }
        catch (OutputException exception)
        {
            // A command that wrote why it stopped, e.g. a prompt on redirected input (InteractionNeededException).
            CurrentRun.Exception = exception;
            return exception.ExitCode;
        }
        catch (UnrecognizedCommandParsingException parsingException)
        {
            // The parser throws before OnParsingComplete; the command is the deepest one it matched.
            CurrentRun.Exception = parsingException;
            CurrentRun.Command = CommandPath.Of(parsingException.Command);
            parsingException.WriteOutput();

            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                "Unrecognized input at {command}; Arguments: {args}; NearestMatches: {nearestMatches}",
                parsingException.Command.Name,
                args,
                parsingException.NearestMatches.ToList())));
            return ExitCodes.ERROR_INPUT_PARSING;
        }
        catch (CommandParsingException parsingException)
        {
            // A missing or an invalid option value, or a single value given twice: the input is wrong, not the tool.
            CurrentRun.Exception = parsingException;
            CurrentRun.Command = CommandPath.Of(parsingException.Command);
            // On standard error, so a redirected standard output stays empty.
            WriteError(PlainOutputExtensions.ToPlainLine(parsingException.Message));
            WriteError($"  What happened, {ParsingErrorResponseMessages.GetRandom()}?");

            // Neither the arguments nor the message, which quotes a value: it may be a value of a key/value pair.
            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                "Invalid input at {command}",
                parsingException.Command.Name)));
            return ExitCodes.ERROR_INPUT_PARSING;
        }
        catch (UnauthorizedAccessException exception)
        {
            CurrentRun.Exception = exception;
            var reportedException = exception.InnerException ?? exception;
            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                reportedException,
                "Unauthorized access; Arguments: {args}",
                [args])));

            Console.Write("! ", Color.Magenta);
            Console.WriteLine(ApiRefusal.GeneralMessage);
            return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
        }
        catch (AuthenticationException exception)
        {
            CurrentRun.Exception = exception;
            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                exception,
                "Sign-in failed ({reason}); Arguments: {args}",
                exception.Reason,
                args)));

            Console.Write("! ", Color.Magenta);
            Console.WriteLine(exception.Message);
            return exception.Reason switch
            {
                AuthenticationFailureReason.Configuration => ExitCodes.ERROR_WRONG_INPUT,
                AuthenticationFailureReason.ServiceError => ExitCodes.ERROR_SERVICE_FAILURE,
                _ => ExitCodes.WARNING_UNAUTHORIZED_ACCESS,
            };
        }
        catch (ApiException exception) when (exception.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            CurrentRun.Exception = exception;
            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                "The API refused the request with {statusCode}; Arguments: {args}",
                exception.StatusCode,
                args)));

            var refusal = ApiRefusal.Of(exception);
            Console.Write("! ", Color.Magenta);
            Console.WriteLine(refusal.Message);

            if (refusal.ErrorCode is not null)
            {
                Console.WriteLine($"  Error code: {refusal.ErrorCode}", Color.DarkGray);
            }

            // A 403 with the server's message, for example a missing machine scope, is not about the credentials.
            if (refusal.MayBeTheCredentials && _machineSignIn is { IsConfigured: true })
            {
                Console.WriteLine(
                    $"  sform signed in as a machine with {string.Join(", ", _machineSignIn.CredentialVariables)}. Check the credentials and the project; account, access point and machine management need a user sign-in.",
                    Color.DarkGray);
            }

            return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
        }
        catch (HttpRequestException exception)
        {
            // The API did not answer at all: a refused connection, a name that does not resolve, a TLS failure.
            return await ReportUnreachableServiceAsync(exception, exception.Message);
        }
        catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            // The timeout of HttpClient.
            return await ReportUnreachableServiceAsync(exception, "The request timed out.");
        }
        catch (Exception exception)
        {
            CurrentRun.Exception = exception;

            // TODO: [P2] Hot fix for bug https://github.com/natemcmaster/CommandLineUtils/issues/541
            if (exception.GetType() == typeof(InvalidOperationException)
                && string.Equals(
                    exception.Message,
                    "Enumeration already finished.",
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("! ", Color.Magenta);
                Console.WriteLine($"Unknown input: {string.Join(' ', args)}");
                Console.WriteLine($"  What happened, {ParsingErrorResponseMessages.GetRandom()}?", Color.DarkGray);
                return ExitCodes.ERROR_INPUT_PARSING;
            }

            CurrentRun.Crashed = true;
            Console.WriteWithGradient("# Total mayhem", Color.Yellow, Color.Magenta);
            Console.WriteLine(", the tool is broken! ):", Color.Magenta);

            if (_loggingOptions is not null && _paths is not null)
            {
                Console.WriteLine("  For more info check the log file at:", Color.DarkGray);
                Console.WriteLine($"    {_paths.GetDailyLogFilePath(_loggingOptions.LogFilePath, DateTime.Now)}", Color.DarkGray);
            }

            await Task.Run(() => LogWithSentry(() => _logger?.LogError(exception, "Unhandled exception occurred at input: {args}", new object[] { args })));
            return ExitCodes.ERROR_CRASH;
        }
    }

    // A service failure, not a crash: the reason, and the address sform called. Neither the arguments nor the URL of the
    // request are logged; an argument may be a value of a key/value pair.
    private static async Task<int> ReportUnreachableServiceAsync(Exception exception, string reason)
    {
        CurrentRun.Exception = exception;
        await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(exception, "The service could not be reached")));

        Console.Write("! ", Color.Magenta);
        Console.WriteLine($"The 2S platform could not be reached: {reason}");

        if (!string.IsNullOrWhiteSpace(_baseUrl))
        {
            Console.WriteLine(
                $"  The API is {_baseUrl} (general.baseUrl, SFORM_general__baseUrl, or STORYTELLER_ENDPOINT for a machine); check the address and the network.",
                Color.DarkGray);
        }

        return ExitCodes.ERROR_SERVICE_FAILURE;
    }

    private static Task<int> RunAsync(string[] args)
    {
        return RunAsync(() => Host.RunCommandLineApplicationAsync(), args);
    }

    private static bool InitializeHost(string[] args)
    {
        try
        {
            _host = CreateHostBuilder(args).Build();
            _logger = _host.Services.GetRequiredService<ILogger<Program>>();
            _loggingOptions = Host.Services.GetService<IOptions<LoggingOptions>>()?.Value;
            _machineSignIn = Host.Services.GetService<MachineSignInSettings>();
            _baseUrl = Host.Services.GetService<IOptions<GeneralOptions>>()?.Value.BaseUrl;
            _telemetry = CommandTelemetry.Create(
                Host.Services.GetService<IOptions<TelemetryOptions>>()?.Value,
                Host.Services.GetRequiredService<IFileSystem>(),
                Host.Services.GetRequiredService<CliPaths>(),
                Host.Services.GetRequiredService<IHostEnvironment>(),
                _machineSignIn,
                _logger);
            return true;
        }
        catch (Exception exception)
        {
            Console.Write("! ", Color.Magenta);
            Console.WriteLine("Totally embarrassing the application failed to initialize, please shout at developers.");
            Console.WriteLine();
            Console.WriteLine(exception.Message);
            Console.WriteLine(exception.StackTrace);
            return false;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        var builder = new HostBuilder();

        // Startup reads them from the properties of the builder.
        if (_paths is not null)
        {
            builder.Properties[typeof(CliPaths)] = _paths;
        }

        return builder
            .UseEnvironment(ResolveEnvironmentName(System.Environment.GetEnvironmentVariable))
            .UseCommandLineApplication<PlatformCommand>(args, ConfigureConsoleApplication)
            .UseStartup<Startup>();
    }

    private static void ConfigureConsoleApplication(CommandLineApplication<PlatformCommand> application)
    {
        application.ResponseFileHandling = ResponseFileHandling.ParseArgsAsLineSeparated;
        application.AllowArgumentSeparator = true;
        application.MakeSuggestionsInErrorMessage = true;
        application.ShortVersionGetter = null;
        application.SetLongVersionGetter();
        application.OnParsingComplete(OnParsingComplete);
    }

    // On the root, it runs for every selected subcommand too, before the command executes. A handler that throws
    // would end the run in place of the command, so nothing escapes.
    private static void OnParsingComplete(ParseResult result)
    {
        try
        {
            CurrentRun.RecordParsing(result.SelectedCommand);
        }
        catch (Exception)
        {
            CurrentRun.Command = CommandPath.UNKNOWN;
        }
    }

    // Only marks the run; the lifetime of the command line host stops the command.
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        CurrentRun.Cancelled = true;
    }

    private static void WriteError(string line)
    {
        System.Console.ForegroundColor = ConsoleColor.DarkGray;

        try
        {
            System.Console.Error.WriteLine(line);
        }
        finally
        {
            System.Console.ResetColor();
        }
    }

    private static void LogWithSentry(Action logOperation)
    {
        if (_loggingOptions is null
            || string.IsNullOrWhiteSpace(_loggingOptions.SentryDsn))
        {
            logOperation();
            return;
        }

        using (SentrySdk.Init(ConfigureSentry))
        {
            logOperation();
        }
    }

    private static void ConfigureSentry(SentryOptions options)
    {
        options.Dsn = _loggingOptions?.SentryDsn;

        // Finds the run in PostHog; with telemetry off there is no id and no tag.
        if (_telemetry.InstallationId is { } installationId)
        {
            options.DefaultTags["posthog_distinct_id"] = installationId;
        }
    }
}
