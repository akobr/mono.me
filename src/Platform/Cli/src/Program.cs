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
using _42.Platform.Cli.ErrorReporting;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    // The failures of the run, to the log file and Sentry; built in InitializeHost for the same reason as the telemetry.
    private static ErrorReporter _reporter = ErrorReporter.None;

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

        // Where requests and the token go is said whenever the environment, not the files, decides it.
        ConfigurationOverrideNotice.Write(System.Environment.GetEnvironmentVariables(), System.Console.Error);

        System.Console.CancelKeyPress += OnCancelKeyPress;
        var exitCode = await RunAsync(args);
        await _telemetry.SendAsync(CurrentRun, exitCode);
        return exitCode;
    }

    // The scheme, host and port of the request that failed when it was not one to the API; null for the API, or when
    // unknown.
    internal static string? UnreachableTargetOf(string? lastTarget, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(lastTarget)
            || !Uri.TryCreate(lastTarget, UriKind.Absolute, out var target)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var api))
        {
            return null;
        }

        return Uri.Compare(target, api, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
            ? null
            : target.GetLeftPart(UriPartial.Authority);
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
    internal static Task<int> RunAsync(Func<Task<int>> run, string[] args)
    {
        return RunAsync(run, args, CurrentRun, _reporter);
    }

    // The same with the record of the run and the reporter given, for a test. A report names the command and the options
    // given, never a value: the run keeps the values of args and of the model, which the reporter scrubs from every text.
    internal static async Task<int> RunAsync(Func<Task<int>> run, string[] args, CommandRun currentRun, ErrorReporter reporter)
    {
        currentRun.RecordArguments(args);

        try
        {
            return await run();
        }
        catch (OutputException exception)
        {
            // A command that wrote why it stopped, e.g. a prompt on redirected input (InteractionNeededException).
            currentRun.Exception = exception;
            return exception.ExitCode;
        }
        catch (UnrecognizedCommandParsingException parsingException)
        {
            // The parser throws before OnParsingComplete; the command is the deepest one it matched.
            currentRun.Exception = parsingException;
            currentRun.RecordFailedParsing(parsingException.Command);
            parsingException.WriteOutput();

            // Neither the message nor the nearest matches: the message quotes the token, which may be a value.
            await reporter.ReportAsync(LogLevel.Warning, "Unrecognized input", currentRun);
            return ExitCodes.ERROR_INPUT_PARSING;
        }
        catch (CommandParsingException parsingException)
        {
            // A missing or an invalid option value, or a single value given twice: the input is wrong, not the tool.
            currentRun.Exception = parsingException;
            currentRun.RecordFailedParsing(parsingException.Command);

            // On standard error, so a redirected standard output stays empty.
            WriteError(PlainOutputExtensions.ToPlainLine(parsingException.Message));
            WriteError($"  What happened, {ParsingErrorResponseMessages.GetRandom()}?");

            // Not the message, which quotes the value.
            await reporter.ReportAsync(LogLevel.Warning, "Invalid input", currentRun);
            return ExitCodes.ERROR_INPUT_PARSING;
        }
        catch (UnauthorizedAccessException exception)
        {
            currentRun.Exception = exception;
            await reporter.ReportAsync(LogLevel.Warning, "Unauthorized access", currentRun, exception.InnerException ?? exception);

            Console.Write("! ", Color.Magenta);
            Console.WriteLine(ApiRefusal.GeneralMessage);
            return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
        }
        catch (AuthenticationException exception)
        {
            currentRun.Exception = exception;
            await reporter.ReportAsync(LogLevel.Warning, $"Sign-in failed ({exception.Reason})", currentRun, exception);

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
            currentRun.Exception = exception;
            await reporter.ReportAsync(LogLevel.Warning, $"The API refused the request with {exception.StatusCode}", currentRun);

            var refusal = ApiRefusal.Of(exception);
            Console.Write("! ", Color.Magenta);
            Console.WriteLine(refusal.Message);

            if (refusal.ErrorCode is not null)
            {
                Console.WriteLine($"  Error code: {refusal.ErrorCode}", Color.DarkGray);
            }

            // The step-up: what a new sign-in takes.
            if (refusal.Hint is not null)
            {
                Console.WriteLine($"  {refusal.Hint}", Color.DarkGray);
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
        catch (ApiException exception) when (exception.StatusCode is >= 400 and < 600)
        {
            // Any other refusal or failure of the API that the command left to Main, for example a 409 or a 501: the
            // server's message and code, and the exit code of its status (2 for a rejected request and a 501 or 505, 3 for
            // a failure of the service). The tool is not broken. The message is not reported: it holds the response.
            currentRun.Exception = exception;
            await reporter.ReportAsync(LogLevel.Warning, $"The API answered {exception.StatusCode}", currentRun);

            var (message, errorCode) = KeyValueConsoleExtensions.ReadError(exception);
            var status = (HttpStatusCode)exception.StatusCode;
            Console.Write("! ", Color.Magenta);
            Console.WriteLine(KeyValueConsoleExtensions.MessageOf(message, status));

            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                Console.WriteLine($"  Error code: {errorCode}", Color.DarkGray);
            }

            return KeyValueConsoleExtensions.ExitCodeOf(status);
        }
        catch (HttpRequestException exception)
        {
            // The API did not answer at all: a refused connection, a name that does not resolve, a TLS failure.
            return await ReportUnreachableServiceAsync(exception, exception.Message, currentRun, reporter);
        }
        catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            // The timeout of HttpClient.
            return await ReportUnreachableServiceAsync(exception, "The request timed out.", currentRun, reporter);
        }
        catch (Exception exception)
        {
            currentRun.Exception = exception;

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

            currentRun.Crashed = true;
            Console.WriteWithGradient("# Total mayhem", Color.Yellow, Color.Magenta);
            Console.WriteLine(", the tool is broken! ):", Color.Magenta);

            if (_loggingOptions is not null && _paths is not null)
            {
                Console.WriteLine("  For more info check the log file at:", Color.DarkGray);
                Console.WriteLine($"    {_paths.GetDailyLogFilePath(_loggingOptions.LogFilePath, DateTime.Now)}", Color.DarkGray);
            }

            await reporter.ReportAsync(LogLevel.Error, "Unhandled exception", currentRun, exception);
            return ExitCodes.ERROR_CRASH;
        }
    }

    // --builtin, which the root gives every command (an inherited option).
    internal static bool IsBuiltInRequested(CommandLineApplication command)
    {
        return command.GetOptions().Any(option => option.LongName == OutputSwitches.BUILTIN_OPTION && option.HasValue());
    }

    // Also the host of CommandConstructionTests, which builds the model of every command through its container.
    internal static IHostBuilder CreateHostBuilder(string[] args, CliPaths? paths)
    {
        var builder = new HostBuilder();

        // Startup reads them from the properties of the builder.
        if (paths is not null)
        {
            builder.Properties[typeof(CliPaths)] = paths;
        }

        return builder
            .UseEnvironment(ResolveEnvironmentName(System.Environment.GetEnvironmentVariable))
            .UseCommandLineApplication<PlatformCommand>(args, ConfigureConsoleApplication)
            .UseStartup<Startup>();
    }

    // Also the configuration of the tests that parse as Main does, with a run of their own.
    internal static void ConfigureConsoleApplication(CommandLineApplication<PlatformCommand> application, CommandRun run)
    {
        application.ResponseFileHandling = ResponseFileHandling.ParseArgsAsLineSeparated;
        application.AllowArgumentSeparator = true;
        application.MakeSuggestionsInErrorMessage = true;
        application.ShortVersionGetter = null;
        application.SetLongVersionGetter();
        application.OnParsingComplete(result => OnParsingComplete(result, run));
    }

    // A service failure, not a crash: the reason, and the address sform called. The report has the exception, never the
    // URL of the request.
    private static async Task<int> ReportUnreachableServiceAsync(Exception exception, string reason, CommandRun currentRun, ErrorReporter reporter)
    {
        currentRun.Exception = exception;
        await reporter.ReportAsync(LogLevel.Warning, "The service could not be reached", currentRun, exception);

        // A request to another service than the API (the sign-in service or its discovery) names that service.
        if (UnreachableTargetOf(RequestTargetHandler.LastTarget, _baseUrl) is { } target)
        {
            Console.Write("! ", Color.Magenta);
            Console.WriteLine($"The service at {target} could not be reached: {reason}");
            Console.WriteLine(
                $"  sform called it for the sign-in, not the API{(string.IsNullOrWhiteSpace(_baseUrl) ? string.Empty : $" ({_baseUrl})")}; check the network and the sign-in settings of the deployment.",
                Color.DarkGray);
            return ExitCodes.ERROR_SERVICE_FAILURE;
        }

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
            var environment = Host.Services.GetRequiredService<IHostEnvironment>();
            _telemetry = CommandTelemetry.Create(
                Host.Services.GetService<IOptions<TelemetryOptions>>()?.Value,
                Host.Services.GetRequiredService<IFileSystem>(),
                Host.Services.GetRequiredService<CliPaths>(),
                environment,
                _machineSignIn,
                _logger);
            _reporter = CreateErrorReporter(environment);
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
        return CreateHostBuilder(args, _paths);
    }

    private static void ConfigureConsoleApplication(CommandLineApplication<PlatformCommand> application)
    {
        ConfigureConsoleApplication(application, CurrentRun);
    }

    // On the root, it runs for every selected subcommand too, before the command executes, and before the handlers of the
    // subcommands write the defaults of the model into their options. A handler that throws would end the run in place of
    // the command, so nothing escapes.
    private static void OnParsingComplete(ParseResult result, CommandRun run)
    {
        try
        {
            run.RecordParsing(result.SelectedCommand);
        }
        catch (Exception)
        {
            run.Command = CommandPath.UNKNOWN;
        }

        try
        {
            if (_host?.Services.GetService<OutputSwitches>() is { } switches)
            {
                switches.IsBuiltInRequested = IsBuiltInRequested(result.SelectedCommand);
            }
        }
        catch (Exception)
        {
            // The console output shows the project settings then, as without the option.
            return;
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

    // Never throws: without a reporter the failures are only printed. The opt-out is read here, not taken from the
    // telemetry, which turns itself off on any failure of its own and would lose it.
    private static ErrorReporter CreateErrorReporter(IHostEnvironment environment)
    {
        try
        {
            var settings = ErrorReportingSettings.Create(
                _loggingOptions,
                TelemetrySettings.IsOptedOutBy(System.Environment.GetEnvironmentVariable),
                _telemetry.InstallationId,
                environment.EnvironmentName.ToLowerInvariant(),
                $"{typeof(Program).Assembly.GetName().Name}@{PostHogCommandTelemetry.CliVersion}",
                Host.Services.GetService<IOptions<AccessDefaultOptions>>()?.Value,
                _machineSignIn,
                Host.Services.GetService<IMachineSignIn>(),
                System.Environment.GetEnvironmentVariables(),
                [System.Environment.UserName, System.Environment.MachineName, System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)]);

            return ErrorReporter.Create(_logger, settings);
        }
        catch (Exception)
        {
            return ErrorReporter.None;
        }
    }
}
