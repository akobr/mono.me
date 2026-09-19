using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

[Command(CommandNames.CA, Description = "Download the CA certificate as PEM.")]
public class CaDownloadCommand : BaseContextCommand
{
    private readonly AccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;

    public CaDownloadCommand(
        IExtendedConsole console,
        ICommandContext context,
        AccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the CA certificate (.pem).")]
    public string? OutputPath { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var pem = await _accessApi.GetCertificateAuthorityPemAsync();

        if (string.IsNullOrWhiteSpace(pem))
        {
            Console.WriteImportant("No CA certificate available.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!string.IsNullOrEmpty(OutputPath))
        {
            _fileSystem.File.WriteAllText(OutputPath, pem);
            Console.WriteImportant($"CA certificate written to {OutputPath}");
        }
        else
        {
            Console.WriteLine(pem);
        }

        return ExitCodes.SUCCESS;
    }
}
