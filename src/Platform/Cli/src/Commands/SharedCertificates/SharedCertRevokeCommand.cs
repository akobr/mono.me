using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.SharedCertificates;

[Command(CommandNames.REVOKE, CommandNames.DELETE, Description = "Revoke a shared certificate.")]
public class SharedCertRevokeCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;

    public SharedCertRevokeCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    [Argument(0, Description = "The thumbprint of the shared certificate to revoke.")]
    public string Thumbprint { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Thumbprint))
        {
            Console.WriteImportant("The thumbprint parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        await _accessApi.RevokeSharedCertificateAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Thumbprint);

        Console.WriteImportant($"Shared certificate {Thumbprint} has been revoked.");
        return ExitCodes.SUCCESS;
    }
}
