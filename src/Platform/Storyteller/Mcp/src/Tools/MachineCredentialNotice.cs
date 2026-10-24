using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Create and reset return credentials the API keeps no copy of. The notice names exactly what the result holds and
// is the first line of the text and the first property of the structured content, so that the model tells the person
// to copy them from the result instead of retyping them.
public static class MachineCredentialNotice
{
    public const string PropertyName = "CredentialNotice";

    public static string? ForCreate(Sdk.MachineAccess machine)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(machine.AccessKey))
        {
            parts.Add($"the {SecretName(machine)} in AccessKey");
        }

        if (!string.IsNullOrEmpty(machine.Certificate))
        {
            parts.Add("the certificate (base64 PKCS#12) in Certificate");
        }

        if (!string.IsNullOrEmpty(machine.CertificatePassword))
        {
            parts.Add("its password in CertificatePassword");
        }

        // A Federated machine's token endpoint is built on every read; only client credentials return it once.
        if (machine.CredentialKind == Sdk.MachineAccessCredentialKind.ClientCredentials)
        {
            if (!string.IsNullOrEmpty(machine.TokenEndpoint))
            {
                parts.Add("the token URL in TokenEndpoint");
            }

            if (!string.IsNullOrEmpty(machine.TokenScope))
            {
                parts.Add("the token scope in TokenScope");
            }
        }

        return Compose(parts);
    }

    public static string? ForReset(Sdk.MachineAccess machine)
    {
        return string.IsNullOrEmpty(machine.AccessKey)
            ? null
            : Compose([$"the new {SecretName(machine)} in AccessKey"]);
    }

    public static CallToolResult Result(Sdk.MachineAccess machine, string? notice)
    {
        if (notice is null)
        {
            return ToolResults.Json(machine);
        }

        var content = (JObject)ToolResults.ToToken(machine);
        content.AddFirst(new JProperty(PropertyName, notice));
        return ToolResults.Json(content, notice);
    }

    private static string SecretName(Sdk.MachineAccess machine)
    {
        return machine.CredentialKind == Sdk.MachineAccessCredentialKind.ClientCredentials ? "client secret" : "API key";
    }

    private static string? Compose(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var them = parts.Count == 1 ? "it" : "them";
        var they = parts.Count == 1 ? "it" : "they";
        return $"The API stores no copy of {EnumNames.Join(parts, "and")}. Copy {them} from this result now; {they} cannot be read again.";
    }
}
