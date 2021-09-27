using System.Reflection;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Init;
using _42.Monorepo.Cli.Commands.New;
using _42.Monorepo.Cli.Commands.Release;
using McMaster.Extensions.CommandLineUtils;
using Console = Colorful.Console;

namespace _42.Monorepo.Cli.Commands
{
    [Command("mrepo", Description = "Mono-repository CLI tooling.")]
    [Subcommand(typeof(InitCommand), typeof(NewCommand), typeof(InfoCommand), typeof(ListCommand), typeof(ReleaseCommand))]
    public class MonorepoCommand : IAsyncCommand
    {
        [Option("-v|--version", CommandOptionType.NoValue, Description = "Display version of this tool.")]
        public bool IsVersionRequested { get; }

        public Task OnExecuteAsync()
        {
            if (IsVersionRequested)
            {
                Console.WriteLine(Assembly.GetCallingAssembly().GetName().Version);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
