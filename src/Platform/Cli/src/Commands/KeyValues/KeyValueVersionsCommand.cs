using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.VERSIONS, Description = "List the stored versions of a key.")]
public class KeyValueVersionsCommand : KeyValueCommandBase
{
    public KeyValueVersionsCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Argument(0, Description = "The key.")]
    public string Key { get; set; } = string.Empty;

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var key = RequireKey(Key);
        var versions = await KeyValues.GetKeyValueVersionsAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key);

        Console.WriteHeader($"Versions of '{key}'");
        Console.WriteKeyValueVersions(versions);
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.DIFF, Description = "Show the text difference between two versions of a key (the latest and the one before by default). Secrets need secret rights.")]
public class KeyValueDiffCommand : KeyValueCommandBase
{
    public KeyValueDiffCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Argument(0, Description = "The key.")]
    public string Key { get; set; } = string.Empty;

    [Argument(1, Description = "The version to compare (to); the latest when missing.")]
    public string? ToVersion { get; set; }

    [Argument(2, Description = "The version to compare from; the one before 'to' when missing (0 is the empty text).")]
    public string? FromVersion { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var key = RequireKey(Key);
        var to = ParseVersion(ToVersion, "to");
        var from = ParseVersion(FromVersion, "from");

        if (to is null && from is not null)
        {
            Console.WriteImportant("Give the 'to' version when you give the 'from' version.");
            throw new WrongInputException();
        }

        if (to is null)
        {
            var versions = await KeyValues.GetKeyValueVersionsAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key);

            if (versions.Count == 0)
            {
                Console.WriteLine($"'{key}' has no versions.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            to = (int)versions.Max(version => version.Version);
        }

        var diff = from is null
            ? await KeyValues.GetKeyValueVersionDiffAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, to.Value)
            : await KeyValues.GetKeyValueVersionDiffCustomAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, to.Value, from.Value);

        Console.WriteDiffResult(diff);
        return ExitCodes.SUCCESS;
    }

    private int? ParseVersion(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text, out var version) && version >= 0)
        {
            return version;
        }

        Console.WriteImportant($"The '{name}' version '{text}' is not a number.");
        throw new WrongInputException();
    }
}
