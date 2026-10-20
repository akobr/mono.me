using System;
using System.Drawing;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
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
    // What the usage event reports; the command line application itself is disposed with the host.
    private static readonly CommandRun CurrentRun = new();

    private static IHost? _host;
    private static ILogger<Program>? _logger;
    private static LoggingOptions? _loggingOptions;

    // Captured before the run: the host is disposed before a command's exception reaches Main.
    private static MachineSignInSettings? _machineSignIn;

    // Built outside the container for the same reason.
    private static ICommandTelemetry _telemetry = NoCommandTelemetry.Instance;

    public static IHost Host => _host ?? throw new InvalidOperationException("The host app is not initialized.");

    public static async Task<int> Main(string[] args)
    {
        CurrentRun.Stopwatch.Start();

        if (!InitializeHost(args))
        {
            return ExitCodes.ERROR_CRASH;
        }

        System.Console.CancelKeyPress += OnCancelKeyPress;
        var exitCode = await RunAsync(args);
        await _telemetry.SendAsync(CurrentRun, exitCode);
        return exitCode;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await Host.RunCommandLineApplicationAsync();
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

            // A 403 with the server's message, for example a missing management scope, is not about the credentials.
            if (refusal.MayBeTheCredentials && _machineSignIn is { IsConfigured: true })
            {
                Console.WriteLine(
                    $"  sform signed in as a machine with {string.Join(", ", _machineSignIn.CredentialVariables)}. Check the credentials and the project; account, access point and machine management need a user sign-in.",
                    Color.DarkGray);
            }

            return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
        }
        catch (Exception exception)
        {
            CurrentRun.Exception = exception;

            if (exception is CommandParsingException parsingException)
            {
                CurrentRun.Command = CommandPath.Of(parsingException.Command);
            }

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

            if (_loggingOptions is not null)
            {
                Console.WriteLine("  For more info check the log file at:", Color.DarkGray);
                Console.WriteLine($"    {_loggingOptions.GetTodayLogFullPath()}", Color.DarkGray);
            }

            await Task.Run(() => LogWithSentry(() => _logger?.LogError(exception, "Unhandled exception occurred at input: {args}", new object[] { args })));
            return ExitCodes.ERROR_CRASH;
        }
    }

    private static bool InitializeHost(string[] args)
    {
        try
        {
            _host = CreateHostBuilder(args).Build();
            _logger = _host.Services.GetRequiredService<ILogger<Program>>();
            _loggingOptions = Host.Services.GetService<IOptions<LoggingOptions>>()?.Value;
            _machineSignIn = Host.Services.GetService<MachineSignInSettings>();
            _telemetry = CommandTelemetry.Create(
                Host.Services.GetService<IOptions<TelemetryOptions>>()?.Value,
                Host.Services.GetRequiredService<IFileSystem>(),
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
        return new HostBuilder()
#if DEBUG
            .UseEnvironment(Environments.Development)
#else
            .UseEnvironment(Environments.Production)
#endif
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
