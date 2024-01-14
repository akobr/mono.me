using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.NEW, "n", Description = "Create new project, workstead, version, mono-repository or add package.")]
    [Subcommand(typeof(NewWorksteadCommand), typeof(NewProjectCommand), typeof(NewPackageCommand), typeof(NewVersionCommand), typeof(NewRepositoryCommand))]
    public class NewCommand : BaseCommand
    {
        private readonly CommandLineApplication application;

        public NewCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context)
        {
            this.application = application;
        }

        protected override Task<int?> ExecutePreconditionsAsync()
        {
            return Task.FromResult<int?>(null);
        }

        protected override Task<int> ExecuteAsync()
        {
            if (!Context.IsValid)
            {
                return application.Commands.ExecuteByNameAsync(CommandNames.REPOSITORY);
            }

            switch (Context.Item.Record.Type)
            {
                case RecordType.Repository:
                    return application.Commands.ExecuteByNameAsync(CommandNames.WORKSTEAD);

                case RecordType.TopWorkstead:
                case RecordType.Workstead:
                case RecordType.Project:
                    return application.Commands.ExecuteByNameAsync(CommandNames.PROJECT);

                default:
                    return Task.FromResult(ExitCodes.ERROR_WRONG_PLACE);
            }
        }
    }
}
