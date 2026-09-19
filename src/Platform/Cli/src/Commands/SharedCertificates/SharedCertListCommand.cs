using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.SharedCertificates;

[Subcommand(
    typeof(SharedCertIssueCommand),
    typeof(SharedCertRevokeCommand))]
[Command(CommandNames.SHARED_CERT, CommandNames.CERTIFICATE, Description = "List and manage shared certificates for a project.")]
public class SharedCertListCommand : BaseContextCommand
{
    private readonly AccessApiClient _accessApi;

    public SharedCertListCommand(
        IExtendedConsole console,
        ICommandContext context,
        AccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    protected override async Task<int> ExecuteAsync()
    {
        var certificates = await _accessApi.GetSharedCertificatesAsync(
            Context.OrganizationName,
            Context.ProjectName);

        Console.WriteHeader("Shared certificates");

        if (certificates.Count < 1)
        {
            Console.WriteLine("No shared certificates issued for this project.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        Console.WriteTable(
            certificates,
            cert => new[]
            {
                cert.Label,
                cert.Thumbprint[..8] + "...",
                cert.NotAfter.ToString("yyyy-MM-dd"),
                cert.IsRevoked ? "REVOKED" : "Active",
            },
            new[] { "Label", "Thumbprint", "Expires", "Status" });

        return ExitCodes.SUCCESS;
    }
}
