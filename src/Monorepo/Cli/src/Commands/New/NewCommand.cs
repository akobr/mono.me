using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Init;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command("new", Description = "Create a new workstead or project.")]
    [Subcommand(typeof(NewWorksteadCommand), typeof(NewProjectCommand), typeof(NewVersionCommand))]
    public class NewCommand : BaseCommand
    {
        public NewCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override Task ExecutePreconditionsAsync()
        {
            return Task.CompletedTask;
        }

        protected override Task ExecuteAsync()
        {
            if (!Context.IsValid)
            {
                return new InitCommand(Console, Context).OnExecuteAsync();
            }

            switch (Context.Item.Record.Type)
            {
                case ItemType.Repository:
                case ItemType.Project:
                    return new NewWorksteadCommand(Console, Context).OnExecuteAsync();

                case ItemType.TopWorkstead:
                case ItemType.Workstead:
                    return new NewProjectCommand(Console, Context).OnExecuteAsync();

                default:
                    return Task.CompletedTask;
            }
        }
    }
}
