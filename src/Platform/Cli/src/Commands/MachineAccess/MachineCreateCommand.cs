using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.CREATE, CommandNames.SET, Description = "Create new machine access.")]
public class MachineCreateCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;

    public MachineCreateCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    [Option("-a|--annotation", CommandOptionType.SingleValue, Description = "An annotation key where the access is restricted.")]
    public string? AnnotationKey { get; set; }

    [Option("-r|--read-only", CommandOptionType.NoValue, Description = "Scope will be read-only. (default)")]
    public bool IsScopeReadOnly { get; set; }

    [Option("-w|--write", CommandOptionType.NoValue, Description = "Scope will be read and write.")]
    public bool IsScopeReadWrite { get; set; }

    [Option("-k|--credential-kind", CommandOptionType.SingleValue, Description = "Credential kind: ApiKey (default), Certificate, CertificateAndApiKey.")]
    public string? CredentialKind { get; set; }

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Certificate lifetime in days (when credential kind includes certificate).")]
    public int? LifetimeDays { get; set; }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the PKCS#12 certificate (.pfx).")]
    public string? OutputPath { get; set; }

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
                    ? MachineAccessCreateScope.DefaultReadWrite
                    : MachineAccessCreateScope.DefaultRead,
            });

        Console.WriteJson(machine);
        Console.WriteLine();

        // Extract certificate fields from AdditionalProperties (until NSwag is regenerated).
        if (machine.AdditionalProperties.TryGetValue("Certificate", out var certObj)
            && certObj is string certificate
            && !string.IsNullOrEmpty(certificate))
        {
            var pkcs12Bytes = Convert.FromBase64String(certificate);
            var filePath = OutputPath ?? $"{machine.Id}.pfx";
            _fileSystem.File.WriteAllBytes(filePath, pkcs12Bytes);

            Console.WriteImportant($"Certificate written to {filePath}");

            if (machine.AdditionalProperties.TryGetValue("CertificatePassword", out var pwdObj)
                && pwdObj is string password
                && !string.IsNullOrEmpty(password))
            {
                Console.WriteImportant($"Certificate password: {password}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }
        else
        {
            Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        }

        return ExitCodes.SUCCESS;
    }
}
