using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands;
using _42.Monorepo.Cli.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Console = Colorful.Console;

namespace _42.Monorepo.Cli
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            ILogger<Program>? logger = null;

            try
            {
                var host = CreateHostBuilder(args).Build();
                logger = host.Services.GetService<ILogger<Program>>();
                return await host.RunCommandLineApplicationAsync();
            }
            catch (UnrecognizedCommandParsingException parsingException)
            {
                parsingException.WriteOutput();

                // TODO: Log event of wrong input (for further data processing and learning)
                logger.LogInformation(
                    "Unrecognized input at {command}; Arguments: {args}; NearestMatches: {nearestMatches}",
                    parsingException.Command.Name,
                    args,
                    parsingException.NearestMatches.ToList());

                return ExitCodes.ERROR_INPUT_PARSING;
            }
            catch (Exception exception)
            {
                Console.WriteLine();
                Console.WriteWithGradient("# Total mayhem", Color.Yellow, Color.Magenta);
                Console.WriteLine(", the tool is broken! ):", Color.Magenta);
                Console.WriteLine("  For more info check the log file at:", Color.DarkGray);
                Console.WriteLine($"    {"todo/file.log"}", Color.DarkGray);

                logger.LogCritical(exception, "Unhandled exception occurred at input: {args}", new object[] { args });
                return ExitCodes.ERROR_CRASH;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return new HostBuilder()
                .UseEnvironment(Environments.Development)
                .UseCommandLineApplication<MonorepoCommand>(args, ConfigureConsoleApplication)
                .UseStartup<Startup>();
        }

        private static void ConfigureConsoleApplication(CommandLineApplication<MonorepoCommand> application)
        {
            application.ResponseFileHandling = ResponseFileHandling.ParseArgsAsLineSeparated;
            application.AllowArgumentSeparator = true;
            application.MakeSuggestionsInErrorMessage = true;
            application.ShortVersionGetter = null;
            application.SetLongVersionGetter();
        }
    }
}
