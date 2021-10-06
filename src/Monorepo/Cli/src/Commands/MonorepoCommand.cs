using System.Reflection;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Init;
using _42.Monorepo.Cli.Commands.New;
using _42.Monorepo.Cli.Commands.Release;
using _42.Monorepo.Cli.Commands.Show;
using McMaster.Extensions.CommandLineUtils;
using Console = Colorful.Console;

namespace _42.Monorepo.Cli.Commands
{
    [Command("mrepo", Description = "Mono-repository CLI tooling.")]
    [Subcommand(typeof(InitCommand), typeof(NewCommand), typeof(InfoCommand), typeof(ListCommand), typeof(ShowCommand), typeof(ReleaseCommand), typeof(ExplainCommand))]
    public class MonorepoCommand : IAsyncCommand
    {
        private readonly CommandLineApplication application;
        private readonly IConsole console;

        public MonorepoCommand(CommandLineApplication application, IConsole console)
        {
            this.application = application;
            this.console = console;
        }

        [Option("-v|--version", CommandOptionType.NoValue, Description = "Display version of this tool.")]
        public bool IsVersionRequested { get; }

        public Task OnExecuteAsync()
        {
            if (IsVersionRequested)
            {
                Console.WriteLine(Assembly.GetCallingAssembly().GetName().Version);
                return Task.CompletedTask;
            }

            application.HelpTextGenerator.Generate(application, console.Out);
            return Task.CompletedTask;
        }
    }
}
