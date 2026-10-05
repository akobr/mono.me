using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.CREATE, CommandNames.SET, Description = "Create new machine access.")]
public class MachineCreateCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;
    private readonly IAuthenticationConfigurationResolver _authenticationConfiguration;

    public MachineCreateCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem,
        IAuthenticationConfigurationResolver authenticationConfiguration)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
        _authenticationConfiguration = authenticationConfiguration;
    }

    [Option("-a|--annotation", CommandOptionType.SingleValue, Description = "An annotation key where the access is restricted.")]
    public string? AnnotationKey { get; set; }

    [Option("-r|--read-only", CommandOptionType.NoValue, Description = "Scope will be read-only. (default)")]
    public bool IsScopeReadOnly { get; set; }

    [Option("-w|--write", CommandOptionType.NoValue, Description = "Scope will be read and write.")]
    public bool IsScopeReadWrite { get; set; }

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
                CertificateLifetimeDays = LifetimeDays,
            });

        Console.WriteJson(new
        {
            machine.Id,
            machine.ObjectId,
            machine.AccessKey,
            machine.Scope,
            machine.AnnotationKey,
            machine.CredentialKind,
            machine.CertificateThumbprint,
            machine.LastRenewalAt,
            Certificate = string.IsNullOrEmpty(machine.Certificate) ? null : "[written to file]",
            CertificatePassword = string.IsNullOrEmpty(machine.CertificatePassword) ? null : "[see below]",
        });
        Console.WriteLine();

        if (!string.IsNullOrEmpty(machine.Certificate))
        {
            var pkcs12Bytes = Convert.FromBase64String(machine.Certificate);
            var filePath = OutputPath ?? $"{machine.Id}.pfx";
            _fileSystem.File.WriteAllBytes(filePath, pkcs12Bytes);

            Console.WriteImportant($"Certificate written to {filePath}");

            if (!string.IsNullOrEmpty(machine.CertificatePassword))
            {
                Console.WriteImportant($"Certificate password: {machine.CertificatePassword}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }
        else if (machine.CredentialKind == MachineAccessCredentialKind.ClientCredentials)
        {
            await WriteClientCredentialsAsync(machine);
        }
        else
        {
            Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        }

        return ExitCodes.SUCCESS;
    }

    // An identity provider client (AuthKit M2M application, Entra ID app registration or Keycloak
    // client): the machine exchanges its client ID and secret for a token.
    private async Task WriteClientCredentialsAsync(_42.Platform.Storyteller.Sdk.MachineAccess machine)
    {
        // The server names the endpoint; older servers only had AuthKit, found through discovery.
        var tokenUrl = string.IsNullOrWhiteSpace(machine.TokenEndpoint)
            ? $"{await GetAuthKitDomainAsync() ?? "https://<your AuthKit domain>"}/oauth2/token"
            : machine.TokenEndpoint;
        var scopeArgument = string.IsNullOrWhiteSpace(machine.TokenScope) ? string.Empty : $" -d scope={machine.TokenScope}";

        Console.WriteLine("Client ID:     ", machine.Id.ThemedHighlight(Console.Theme));
        Console.WriteLine("Client secret: ", machine.AccessKey.ThemedHighlight(Console.Theme));
        Console.WriteLine("Token URL:     ", tokenUrl.ThemedHighlight(Console.Theme));

        if (!string.IsNullOrWhiteSpace(machine.TokenScope))
        {
            Console.WriteLine("Token scope:   ", machine.TokenScope.ThemedHighlight(Console.Theme));
        }

        Console.WriteLine();
        Console.WriteLine("Get a token (send it as Authorization: Bearer <access_token>):");
        Console.WriteLine($"  curl -s -X POST {tokenUrl} -d grant_type=client_credentials -d client_id={machine.Id} -d client_secret=$CLIENT_SECRET{scopeArgument}".ThemedLowlight(Console.Theme));
        Console.WriteLine();
        Console.WriteImportant("Make sure to copy the client secret, it is not stored anywhere.");
    }

    private async Task<string?> GetAuthKitDomainAsync()
    {
        try
        {
            var settings = await _authenticationConfiguration.ResolveAsync();
            return string.IsNullOrWhiteSpace(settings.AuthKitDomain) ? null : settings.AuthKitDomain.TrimEnd('/');
        }
        catch (AuthenticationException)
        {
            return null;
        }
    }
}
