using System.Text;
using _42.CLI.Toolkit;
using _42.Platform.Storyteller.Sdk.Configuring;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        // A document holds any text; the default encoding of a Windows console or a redirected output would garble it.
        System.Console.OutputEncoding = new UTF8Encoding(false);

        // STORYTELLER_ENDPOINT, STORYTELLER_KEY and the variables of the machine's credential come from the environment:
        // from the shell, or from an AppHost such as DotNet.AspireHost.
        return RunAsync(args, PhysicalConsole.Singleton, services => services.AddStoryteller());
    }

    public static async Task<int> RunAsync(string[] args, IConsole console, Action<IServiceCollection> addStoryteller)
    {
        try
        {
            return await new HostBuilder()
                .ConfigureLogging(logging => logging

                    // On standard error, so standard output holds only what the command renders.
                    .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
                    .SetMinimumLevel(LogLevel.Warning))
                .ConfigureServices(services =>
                {
                    services.AddSingleton(console);
                    services.AddCliToolkit();
                    addStoryteller(services);
                })
                .RunCommandLineApplicationAsync<ExecutorCommand>(args);
        }
        catch (Exception exception) when (exception is CommandParsingException or StorytellerConfigurationException)
        {
            // An unknown option, or settings that the registration refused. The messages name what to fix, never a value.
            console.Error.WriteLine(exception.Message);
            return ExecutorCommand.WrongInput;
        }
    }
}
