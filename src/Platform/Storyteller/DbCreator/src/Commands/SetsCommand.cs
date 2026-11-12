using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Storyteller.DbCreator.DataSets;
using McMaster.Extensions.CommandLineUtils;
using Console = Colorful.Console;

namespace _42.Platform.Storyteller.DbCreator.Commands;

[Command(Name, Description = "List the data sets: the built-in ones and those of the --path folders, with what they hold. Reads the files only.")]
public class SetsCommand : IAsyncCommand
{
    public const string Name = "sets";

    private readonly IFileSystem _fileSystem;
    private readonly DataSetCatalog _catalog;

    public SetsCommand(IFileSystem fileSystem, DataSetCatalog catalog)
    {
        _fileSystem = fileSystem;
        _catalog = catalog;
    }

    [Option("--path", CommandOptionType.MultipleValue, ValueName = "folder", Description = "A folder with set.json, or a folder of such folders; its sets join the built-in ones. Repeatable.")]
    public string[]? Paths { get; set; }

    public Task<int> OnExecuteAsync()
    {
        try
        {
            var sets = _catalog.GetSets(Paths ?? []);

            if (sets.Count == 0)
            {
                Console.WriteLine($"No sets: '{_catalog.BuiltInFolder}' holds none, and no --path is given.");
                return Task.FromResult(ExitCodes.SUCCESS);
            }

            var names = sets.Select(set => set.Name).ToHashSet();

            foreach (var set in sets)
            {
                DataSetOutput.WriteOverview(DataSetOverview.Read(_fileSystem, set), names);
            }

            return Task.FromResult(ExitCodes.SUCCESS);
        }
        catch (DataSetException exception)
        {
            DataSetOutput.WriteWrongInput(exception);
            return Task.FromResult(ExitCodes.ERROR_WRONG_INPUT);
        }
    }
}
