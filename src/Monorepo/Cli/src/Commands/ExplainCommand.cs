using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.EXPLAIN, Description = "Display explanation of any item inside the mono-repository.")]
    public class ExplainCommand : BaseCommand
    {
        public ExplainCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, "path", Description = "Path to a file/directory in the mono-repository.")]
        public string? Path { get; set; } = string.Empty;

        protected override Task<int> ExecuteAsync()
        {
            if (string.IsNullOrWhiteSpace(Path)
                || Path.EqualsOrdinalIgnoreCase("/")
                || Path.EqualsOrdinalIgnoreCase("repo")
                || Path.EqualsOrdinalIgnoreCase("mrepo"))
            {
                Console.WriteLine("Under construction (:");
            }

            string name = (System.IO.Path.GetFileName(Path) ?? Path ?? string.Empty).ToLowerInvariant();

            switch (name)
            {
                case "packages.props":
                    break;

                case "mrepo.json":
                    break;

                case "version.json":
                    break;
            }

            return Task.FromResult(ExitCodes.SUCCESS);
        }
    }
}
