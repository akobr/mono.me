using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.CREATE, CommandNames.SET, Description = "Create new machine access.")]
public class MachineCreateCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public MachineCreateCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    [Option("-a|--annotation", CommandOptionType.SingleValue, Description = "An annotation key where the access is restricted.")]
    public string? AnnotationKey { get; set; }

    [Option("-r|--read-only", CommandOptionType.NoValue, Description = "Scope will be read-only. (default)")]
    public bool IsScopeReadOnly { get; set; }

    [Option("-w|--write", CommandOptionType.NoValue, Description = "Scope will be read and write.")]
    public bool IsScopeReadWrite { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!string.IsNullOrWhiteSpace(AnnotationKey))
        {
            Console.ValidateAnnotationKey(AnnotationKey);
        }

        var machine = await _apiClient.V1[Context.OrganizationName][Context.ProjectName].Access.Machines.PostAsync(
            new MachineAccessCreate
            {
                Organization = Context.OrganizationName,
                Project = Context.ProjectName,
                AnnotationKey = AnnotationKey,
                Scope = IsScopeReadWrite
                    ? MachineAccessCreate_Scope.DefaultReadWrite
                    : MachineAccessCreate_Scope.DefaultRead,
            });

        Console.WriteJson(machine);
        Console.WriteLine();
        Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        return ExitCodes.SUCCESS;
    }
}
