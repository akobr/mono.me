using System;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Machine credential kinds as the CLI accepts them: the enum name in any case, or kebab case
// (client-credentials). The SDK has one enum per property, all with the same names.
internal static class CredentialKinds
{
    public const string Names = "ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated";

    // Enum.TryParse accepts any number; only the named kinds may reach the server.
    public static bool TryParse<TKind>(string? value, out TKind kind)
        where TKind : struct, Enum
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim().Replace("-", string.Empty), ignoreCase: true, out kind)
            && Enum.IsDefined(kind);
    }

    public static TTarget Convert<TTarget>(Enum kind)
        where TTarget : struct, Enum
    {
        return Enum.Parse<TTarget>(kind.ToString());
    }
}
