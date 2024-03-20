using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using _42.Platform.Sdk.Model;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.CREATE, CommandNames.SET, Description = "Create new machine access.")]
public class MachineCreateCommand : BaseContextCommand
{
    private readonly IAccessApiAsync _accessApi;

    public MachineCreateCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiAsync accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
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

        var machine = await _accessApi.CreateMachineAccessAsync(
            Context.OrganizationName,
            Context.ProjectName,
            new MachineAccessCreate
            {
                Organization = Context.OrganizationName,
                Project = Context.ProjectName,
                AnnotationKey = AnnotationKey,
                Scope = IsScopeReadWrite
                    ? MachineAccessCreate.ScopeEnum.DefaultReadWrite
                    : MachineAccessCreate.ScopeEnum.DefaultRead,
            });

        Console.WriteJson(machine);
        Console.WriteLine();
        Console.WriteImportant("Please copy the access key (secret), it is not stored anywhere.");
        return ExitCodes.SUCCESS;
    }
}
