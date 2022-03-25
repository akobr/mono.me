using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.NEW, Description = "Create new project, workstead, version or mono-repository.")]
    [Subcommand(typeof(NewWorksteadCommand), typeof(NewProjectCommand), typeof(NewVersionCommand), typeof(NewRepositoryCommand))]
    public class NewCommand : BaseCommand
    {
        private readonly CommandLineApplication application;

        public NewCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context)
        {
            this.application = application;
        }

        protected override Task ExecutePreconditionsAsync()
        {
            return Task.CompletedTask;
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
