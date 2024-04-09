using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Cli;
using _42.Platform.Storyteller.Simulator.Commands;
using _42.Platform.Storyteller.Simulator.Configuration;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;
using Console = Colorful.Console;

namespace _42.Platform.Storyteller.Simulator;

public class Program
{
    private static IHost? _host;
    private static ILogger<Program>? _logger;
    private static LoggingOptions? _loggingOptions;

    public static IHost Host => _host ?? throw new InvalidOperationException("The host app is not initialized.");

    public static async Task<int> Main(string[] args)
    {
        if (!InitializeHost(args))
        {
            return ExitCodes.ERROR_CRASH;
        }

        try
        {
            return await Host.RunCommandLineApplicationAsync();
        }
        catch (UnrecognizedCommandParsingException parsingException)
        {
            parsingException.WriteOutput();

            await Task.Run(() => LogWithSentry(() => _logger?.LogWarning(
                "Unrecognized input at {command}; Arguments: {args}; NearestMatches: {nearestMatches}",
                parsingException.Command.Name,
                args,
                parsingException.NearestMatches.ToList())));
            return ExitCodes.ERROR_INPUT_PARSING;
        }
        catch (Exception exception)
        {
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
            .UseCommandLineApplication<InitDbCommand>(args, ConfigureConsoleApplication)
            .UseStartup<Startup>();
    }

    private static void ConfigureConsoleApplication(CommandLineApplication<InitDbCommand> application)
    {
        application.ResponseFileHandling = ResponseFileHandling.ParseArgsAsLineSeparated;
        application.AllowArgumentSeparator = true;
        application.MakeSuggestionsInErrorMessage = true;
        application.ShortVersionGetter = null;
        application.SetLongVersionGetter();
    }

    private static void LogWithSentry(Action logOperation)
    {
        if (_loggingOptions is null
            || string.IsNullOrWhiteSpace(_loggingOptions.SentryDsn))
        {
            logOperation();
            return;
        }

        using (SentrySdk.Init(_loggingOptions.SentryDsn))
        {
            logOperation();
        }
    }
}
