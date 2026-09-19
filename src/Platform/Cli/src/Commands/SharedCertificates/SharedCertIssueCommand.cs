using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.SharedCertificates;

[Command(CommandNames.ISSUE, CommandNames.CREATE, Description = "Issue a new shared certificate for a project.")]
public class SharedCertIssueCommand : BaseContextCommand
{
    private readonly AccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;

    public SharedCertIssueCommand(
        IExtendedConsole console,
        ICommandContext context,
        AccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "A label for the shared certificate.")]
    public string Label { get; set; } = string.Empty;

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Certificate lifetime in days.")]
    public int? LifetimeDays { get; set; }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the PKCS#12 certificate (.pfx).")]
    public string? OutputPath { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            Console.WriteImportant("The label parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var certificate = await _accessApi.IssueSharedCertificateAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Label,
            LifetimeDays);

        Console.WriteImportant($"Shared certificate issued: {certificate.Label}");
        Console.WriteLine($"  Thumbprint: {certificate.Thumbprint}");
        Console.WriteLine($"  Valid: {certificate.NotBefore:yyyy-MM-dd} to {certificate.NotAfter:yyyy-MM-dd}");

        if (!string.IsNullOrEmpty(certificate.Certificate))
        {
            var pkcs12Bytes = Convert.FromBase64String(certificate.Certificate);
            var filePath = OutputPath ?? $"shared-{certificate.Label}.pfx";
            _fileSystem.File.WriteAllBytes(filePath, pkcs12Bytes);

            Console.WriteImportant($"Certificate written to {filePath}");

            if (!string.IsNullOrEmpty(certificate.CertificatePassword))
            {
                Console.WriteImportant($"Certificate password: {certificate.CertificatePassword}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }

        return ExitCodes.SUCCESS;
    }
}
