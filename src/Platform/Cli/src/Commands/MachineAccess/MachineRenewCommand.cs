using System;
using System.IO.Abstractions;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Sharprompt;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.RENEW, Description = "Renew certificate for a machine access.")]
public class MachineRenewCommand : BaseContextCommand
{
    private readonly IServiceProvider _services;
    private readonly ISdkConfiguration _sdkConfiguration;
    private readonly IFileSystem _fileSystem;

    public MachineRenewCommand(
        IExtendedConsole console,
        ICommandContext context,
        IServiceProvider services,
        ISdkConfiguration sdkConfiguration,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _services = services;
        _sdkConfiguration = sdkConfiguration;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The id of the machine access to renew.")]
    public string MachineId { get; set; } = string.Empty;

    [Option("-c|--certificate", CommandOptionType.SingleValue, Description = "Path to the PKCS#12 (.pfx) certificate file for mTLS authentication.")]
    public string? CertificatePath { get; set; }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "Output file path for the renewed PKCS#12 certificate (.pfx).")]
    public string? OutputPath { get; set; }

    [Option("--password-stdin", CommandOptionType.NoValue, Description = "Read the password of the -c certificate from the first line of the standard input (an empty line for none) instead of asking.")]
    public bool PasswordFromStdin { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            Console.WriteImportant("The machine id parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        if (PasswordFromStdin && string.IsNullOrWhiteSpace(CertificatePath))
        {
            Console.WriteImportant("--password-stdin reads the password of the certificate given with -c|--certificate.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!string.IsNullOrWhiteSpace(CertificatePath))
        {
            var password = ReadCertificatePassword();
            var cert = X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, password);
            ((SdkConfiguration)_sdkConfiguration).ClientCertificate = cert;
        }

        var accessApi = _services.GetRequiredService<IAccessApiClient>();
        CertificateRenewalResult result;

        try
        {
            result = await accessApi.RenewMachineCertificateAsync(
                Context.OrganizationName,
                Context.ProjectName,
                MachineId);
        }
        catch (ApiException exception) when (exception.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            // 400 NotInRenewalWindow names when the window opens, 404 a certificate that is not the machine's current
            // one: the server's message, exit code 2; a 401 (another machine, no certificate) goes to Program.Main
            return Console.WriteApiError(exception);
        }

        if (!string.IsNullOrEmpty(result.Message))
        {
            Console.WriteImportant(result.Message);
        }

        var (pkcs12, renewedPassword) = ReadCertificate(result);

        if (pkcs12 is { Length: > 0 })
        {
            var filePath = OutputPath ?? $"{MachineId}-renewed.pfx";
            _fileSystem.File.WriteAllBytes(filePath, pkcs12);

            Console.WriteImportant($"Renewed certificate written to {filePath}");

            if (!string.IsNullOrEmpty(renewedPassword))
            {
                Console.WriteImportant($"Certificate password: {renewedPassword}");
                Console.WriteImportant("Make sure to copy the password, it is not stored anywhere.");
            }
        }
        else if (result.LastRenewalAt.HasValue)
        {
            Console.WriteLine($"Last renewed at: {result.LastRenewalAt.Value:O}");
        }

        return ExitCodes.SUCCESS;
    }

    // The renewed PKCS#12 and its password: Pkcs12 and Password of the documented CertificateRenewalResult, or, from an
    // API deployed before 424-215 that answered with an anonymous object, its base64 Certificate and CertificatePassword
    // (which the client keeps in AdditionalProperties).
    private static (byte[]? Pkcs12, string? Password) ReadCertificate(CertificateRenewalResult result)
    {
        if (result.Pkcs12 is { Length: > 0 })
        {
            return (result.Pkcs12, result.Password);
        }

        var extra = result.AdditionalProperties;

        if (!extra.TryGetValue("Certificate", out var certificate)
            || ReadString(certificate) is not { Length: > 0 } base64)
        {
            return (null, null);
        }

        try
        {
            return (Convert.FromBase64String(base64), extra.TryGetValue("CertificatePassword", out var secret) ? ReadString(secret) : null);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(object? value)
    {
        return value switch
        {
            string text => text,
            JValue { Type: JTokenType.String } token => (string?)token,
            _ => null,
        };
    }

    // Standard input is read only when --password-stdin asks for it, and only its first line, so a pipe that stays open
    // does not keep sform waiting. Without it, a redirected input stops the command before any call.
    private string ReadCertificatePassword()
    {
        if (PasswordFromStdin)
        {
            return Console.Console.In.ReadLine() ?? string.Empty;
        }

        Console.EnsureInteractiveValue(
            "the password of the certificate",
            "Pass it as the first line of the standard input with --password-stdin (an empty line for none).");

        return Console.Password(new PasswordOptions
        {
            Message = "Certificate password (press Enter if none)",
        });
    }
}
