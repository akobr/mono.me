using System;
using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.Telemetry;
using McMaster.Extensions.CommandLineUtils;
using Moq;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The command tree of sform as the host builds it, without its services: every constructor dependency is a mock.
internal static class SformApplication
{
    // With a run, configured as Program.ConfigureConsoleApplication does: response files, the separator, and the run
    // recorded from the root's OnParsingComplete, before the handlers of the subcommands.
    public static CommandLineApplication<PlatformCommand> Create(CommandRun? run = null)
    {
        var app = new CommandLineApplication<PlatformCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(new MockingServiceProvider());

        if (run is not null)
        {
            app.ResponseFileHandling = ResponseFileHandling.ParseArgsAsLineSeparated;
            app.AllowArgumentSeparator = true;
            app.MakeSuggestionsInErrorMessage = true;
            app.OnParsingComplete(result => run.RecordParsing(result.SelectedCommand));
        }

        return app;
    }

    private sealed class MockingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            // The settings of the process are plain classes, not mocked.
            var paths = CliPaths.Create(new MockFileSystem(), null, MockUnixSupport.Path(@"C:\tools\sform"), () => MockUnixSupport.Path(@"C:\Users\ada"));

            if (serviceType == typeof(CliPaths))
            {
                return paths;
            }

            if (serviceType == typeof(SformEnvironment))
            {
                return SformEnvironment.FromEnvironment(_ => null, paths);
            }

            if (serviceType == typeof(TransferPolling))
            {
                return new TransferPolling();
            }

            return serviceType.IsInterface || serviceType.IsAbstract
                ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(serviceType))!).Object
                : null;
        }
    }
}
