using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.RENEW, Description = "Renew certificate for a machine access.")]
public class MachineRenewCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;

    public MachineRenewCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The id of the machine access to renew.")]
    public string MachineId { get; set; } = string.Empty;

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the renewed PKCS#12 certificate (.pfx).")]
    public string? OutputPath { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            Console.WriteImportant("The machine id parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var result = await _accessApi.RenewMachineCertificateAsync(
            Context.OrganizationName,
            Context.ProjectName,
            MachineId);

        if (!string.IsNullOrEmpty(result.Message))
        {
            Console.WriteImportant(result.Message);
        }

        if (result.Pkcs12 is { Length: > 0 })
        {
            var filePath = OutputPath ?? $"{MachineId}-renewed.pfx";
            _fileSystem.File.WriteAllBytes(filePath, result.Pkcs12);

            Console.WriteImportant($"Renewed certificate written to {filePath}");

            if (!string.IsNullOrEmpty(result.Password))
            {
                Console.WriteImportant($"Certificate password: {result.Password}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }
        else if (result.LastRenewalAt.HasValue)
        {
            Console.WriteLine($"Last renewed at: {result.LastRenewalAt.Value:O}");
        }

        return ExitCodes.SUCCESS;
    }
}
