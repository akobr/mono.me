using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.CREATE, CommandNames.SET, Description = "Create new machine access.")]
public class MachineCreateCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;
    private readonly IAuthenticationConfigurationResolver _authenticationConfiguration;
    private readonly GeneralOptions _general;

    public MachineCreateCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem,
        IAuthenticationConfigurationResolver authenticationConfiguration,
        IOptions<GeneralOptions> generalOptions)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
        _authenticationConfiguration = authenticationConfiguration;
        _general = generalOptions.Value;
    }

    [Option("-a|--annotation", CommandOptionType.SingleValue, Description = "An annotation key where the access is restricted.")]
    public string? AnnotationKey { get; set; }

    [Option("-r|--read-only", CommandOptionType.NoValue, Description = "Scope will be read-only. (default)")]
    public bool IsScopeReadOnly { get; set; }

    [Option("-w|--write", CommandOptionType.NoValue, Description = "Scope will be read and write.")]
    public bool IsScopeReadWrite { get; set; }

    [Option("-k|--credential-kind", CommandOptionType.SingleValue, Description = "One of the kinds the project enables: " + CredentialKinds.Names + ". Default: the project's default kind.")]
    public string? CredentialKind { get; set; }

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Certificate lifetime in days (when credential kind includes certificate). Default: the project's default lifetime.")]
    public int? LifetimeDays { get; set; }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the PKCS#12 certificate (.pfx).")]
    public string? OutputPath { get; set; }

    [Option("-s|--shared-certificate", CommandOptionType.NoValue, Description = "A certificate-and-api-key machine without a certificate of its own: it signs in with a shared certificate of the project and its API key.")]
    public bool UseSharedCertificate { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!string.IsNullOrWhiteSpace(AnnotationKey))
        {
            Console.ValidateAnnotationKey(AnnotationKey);
        }

        MachineAccessCreateCredentialKind? credentialKind = null;

        if (CredentialKind is not null)
        {
            if (!CredentialKinds.TryParse<MachineAccessCreateCredentialKind>(CredentialKind, out var kind))
            {
                Console.WriteImportant($"Unknown credential kind '{CredentialKind}'. Use {CredentialKinds.Names}.");
                return ExitCodes.ERROR_INPUT_PARSING;
            }

            credentialKind = kind;
        }

        if (UseSharedCertificate)
        {
            // A shared certificate is the certificate half of a CertificateAndApiKey login.
            if (credentialKind is not null and not MachineAccessCreateCredentialKind.CertificateAndApiKey)
            {
                Console.WriteImportant($"A shared certificate is used only by certificate-and-api-key machines, not {credentialKind}.");
                return ExitCodes.ERROR_INPUT_PARSING;
            }

            credentialKind = MachineAccessCreateCredentialKind.CertificateAndApiKey;

            if (LifetimeDays is not null || OutputPath is not null)
            {
                Console.WriteLine("The machine gets no certificate of its own, so --lifetime-days and --output are ignored.");
            }
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

                // Null: the project's default kind and lifetime.
                CredentialKind = credentialKind,
                CertificateLifetimeDays = UseSharedCertificate ? null : LifetimeDays,
                UseSharedCertificate = UseSharedCertificate ? true : null,
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
            machine.CertificateLifetimeDays,
            machine.CertificateNotAfter,
            machine.LastRenewalAt,
            machine.UsesSharedCertificate,
            Certificate = string.IsNullOrEmpty(machine.Certificate) ? null : "[written to file]",
            CertificatePassword = string.IsNullOrEmpty(machine.CertificatePassword) ? null : "[see below]",
        });
        Console.WriteLine();

        string? certificatePath = null;
        string? tokenUrl = null;

        if (!string.IsNullOrEmpty(machine.Certificate))
        {
            var pkcs12Bytes = Convert.FromBase64String(machine.Certificate);
            var filePath = OutputPath ?? $"{machine.Id}.pfx";
            _fileSystem.File.WriteAllBytes(filePath, pkcs12Bytes);
            certificatePath = _fileSystem.Path.GetFullPath(filePath) ?? filePath;

            Console.WriteImportant($"Certificate written to {filePath}");

            if (!string.IsNullOrEmpty(machine.CertificatePassword))
            {
                Console.WriteImportant($"Certificate password: {machine.CertificatePassword}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }
        else if (machine.CredentialKind == MachineAccessCredentialKind.ClientCredentials)
        {
            tokenUrl = await WriteClientCredentialsAsync(machine);
        }
        else
        {
            Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        }

        if (machine.UsesSharedCertificate)
        {
            Console.WriteLine("The machine has no certificate of its own: it presents a shared certificate of the project with its API key (sform account shared-cert).");
        }

        WritePipelineVariables(machine, certificatePath, tokenUrl);
        return ExitCodes.SUCCESS;
    }

    // The STORYTELLER_* variables of a non-interactive sform sign-in as this machine. Secrets point at the
    // value printed above instead of being printed twice.
    private void WritePipelineVariables(_42.Platform.Storyteller.Sdk.MachineAccess machine, string? certificatePath, string? tokenUrl)
    {
        var variables = new List<string> { $"{StorytellerVariables.API_URL}={_general.BaseUrl}" };
        var kind = machine.CredentialKind;

        if (kind == MachineAccessCredentialKind.ClientCredentials)
        {
            variables.Add($"{StorytellerVariables.PROJECT}={Context.OrganizationName}.{Context.ProjectName}");
            variables.Add($"{StorytellerVariables.CLIENT_ID}={machine.Id}");
            variables.Add($"{StorytellerVariables.CLIENT_SECRET}=<client secret above>");

            if (tokenUrl is not null)
            {
                variables.Add($"{StorytellerVariables.TOKEN_URL}={tokenUrl}");
            }

            if (!string.IsNullOrWhiteSpace(machine.TokenScope))
            {
                variables.Add($"{StorytellerVariables.TOKEN_SCOPE}={machine.TokenScope}");
            }
        }

        if (kind is MachineAccessCredentialKind.ApiKey or MachineAccessCredentialKind.CertificateAndApiKey)
        {
            variables.Add($"{StorytellerVariables.API_KEY}=<access key above>");
        }

        if (kind is MachineAccessCredentialKind.Certificate or MachineAccessCredentialKind.CertificateAndApiKey)
        {
            if (machine.UsesSharedCertificate)
            {
                variables.Add($"{StorytellerVariables.CERTIFICATE_FILE}=<shared certificate .pfx>");
                variables.Add($"{StorytellerVariables.CERTIFICATE_PASSWORD}=<its password>");
            }
            else
            {
                variables.Add($"{StorytellerVariables.CERTIFICATE_FILE}={certificatePath ?? "<certificate .pfx>"}");

                if (!string.IsNullOrEmpty(machine.CertificatePassword))
                {
                    variables.Add($"{StorytellerVariables.CERTIFICATE_PASSWORD}=<certificate password above>");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("To sign in sform in a pipeline, set:");

        foreach (var variable in variables)
        {
            Console.WriteLine($"  {variable}");
        }
    }

    // An identity provider client (AuthKit M2M application, Entra ID app registration or Keycloak
    // client): the machine exchanges its client ID and secret for a token. Returns the token URL
    // when it is known.
    private async Task<string?> WriteClientCredentialsAsync(_42.Platform.Storyteller.Sdk.MachineAccess machine)
    {
        // The server names the endpoint; older servers only had AuthKit, found through discovery.
        var domain = string.IsNullOrWhiteSpace(machine.TokenEndpoint) ? await GetAuthKitDomainAsync() : null;
        var knownTokenUrl = string.IsNullOrWhiteSpace(machine.TokenEndpoint)
            ? domain is null ? null : $"{domain}/oauth2/token"
            : machine.TokenEndpoint;
        var tokenUrl = knownTokenUrl ?? "https://<your AuthKit domain>/oauth2/token";
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
        return knownTokenUrl;
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
