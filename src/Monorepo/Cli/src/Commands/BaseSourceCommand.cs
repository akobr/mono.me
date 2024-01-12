using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Model;

namespace _42.Monorepo.Cli.Commands;

public abstract class BaseSourceCommand : BaseCommand
{
    protected BaseSourceCommand(IExtendedConsole console, ICommandContext context)
        : base(console, context)
    {
        // no operation
    }

    protected override async Task<int?> ExecutePreconditionsAsync()
    {
        await base.ExecutePreconditionsAsync();

        if (Context.Item.Record.Type > RecordType.Project)
        {
            Console.WriteImportant($"This command is available only for source content (in {Constants.SOURCE_DIRECTORY_NAME} directory).");
            return ExitCodes.ERROR_WRONG_PLACE;
        }

        return null;
    }
}
