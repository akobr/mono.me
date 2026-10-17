using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Sets the whole trusted issuer, like the issuer PUT: settings not given are cleared. The keys come from the
// issuer's discovery document unless --jwks-uri or --jwks-file says otherwise.
[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or replace a trusted issuer of the project (Administrator): settings not given are cleared, and the issuer URL of an existing one cannot change.")]
public class MachineIssuerSetCommand : FederationCommandBase
{
    private readonly IFileSystem _fileSystem;

    public MachineIssuerSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context, accessApi)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The name of the trusted issuer, which rules reference: 1 to 63 lower-case letters, digits or hyphens.")]
    public string Name { get; set; } = string.Empty;

    [Option("--issuer", CommandOptionType.SingleValue, Description = "The exact iss of the platform's tokens, for example https://token.actions.githubusercontent.com (case and a trailing / matter).")]
    public string? Issuer { get; set; }

    [Option("--jwks-uri", CommandOptionType.SingleValue, Description = "Fetch the keys from this JSON Web Key Set URL instead of the issuer's discovery document.")]
    public string? JwksUri { get; set; }

    [Option("--jwks-file", CommandOptionType.SingleValue, Description = "Store the JSON Web Key Set (public keys) of this file instead of fetching keys, for example from kubectl get --raw /openid/v1/jwks.")]
    public string? JwksFile { get; set; }

    [Option("--max-lifetime", CommandOptionType.SingleValue, Description = "The longest accepted lifetime (exp - iat) of a platform token in seconds, 60 to 86400. Default: 3600.")]
    public int? MaxLifetimeSeconds { get; set; }

    [Option("--require-claim", CommandOptionType.MultipleValue, Description = "A claim every token of the issuer must carry, key=value, for example repository_owner_id=65. Repeat it; values of one key are alternatives.")]
    public string[]? RequiredClaims { get; set; }

    [Option("--description", CommandOptionType.SingleValue, Description = "A description, at most 256 characters.")]
    public string? IssuerDescription { get; set; }

    protected override async Task<int> ExecuteFederationAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Console.WriteImportant("The name parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            Console.WriteImportant("The --issuer is required: the exact iss of the platform's tokens.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        if (JwksUri is not null && JwksFile is not null)
        {
            Console.WriteImportant("Give either --jwks-uri or --jwks-file, not both.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        if (!ClaimOptions.TryParse(RequiredClaims, "--require-claim", out var requiredClaims, out var error))
        {
            Console.WriteImportant(error);
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        string? jwks = null;

        if (JwksFile is not null)
        {
            if (!_fileSystem.File.Exists(JwksFile))
            {
                Console.WriteImportant($"The JWKS file {JwksFile} does not exist.");
                return ExitCodes.ERROR_INPUT_PARSING;
            }

            jwks = await _fileSystem.File.ReadAllTextAsync(JwksFile);
        }

        var name = Name.Trim();
        var issuer = await AccessApi.SetFederatedIssuerAsync(
            Context.OrganizationName,
            Context.ProjectName,
            name,
            new FederatedIssuer
            {
                Name = name,
                Issuer = Issuer.Trim(),
                KeySource = JwksFile is not null
                    ? FederatedIssuerKeySource.Jwks
                    : JwksUri is not null ? FederatedIssuerKeySource.JwksUri : FederatedIssuerKeySource.Discovery,
                JwksUri = JwksUri?.Trim(),
                Jwks = jwks,
                MaxTokenLifetimeSeconds = MaxLifetimeSeconds,
                RequiredClaims = requiredClaims,
                Description = IssuerDescription,
            });

        Console.WriteImportant($"Trusted issuer {issuer.Name ?? name} of {Context.OrganizationName}.{Context.ProjectName}:");
        Console.WriteFederatedIssuer(issuer);
        return ExitCodes.SUCCESS;
    }
}
