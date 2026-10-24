using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The protection levels of the values store. Every level but Public is a secret. Hyok values are encrypted by the
// client for the project's recipients; the platform stores the JWE document and cannot read it.
public static class KeyValueProtections
{
    public const string ClientEncryptedProperty = "ClientEncrypted";

    public const string HyokRefusal =
        "'protection' Hyok is not written by this server: a client-encrypted value has to be encrypted for the project's "
        + "recipients before it is sent, which sform (sform story kv set <key> --protection hyok) and the admin UI do on the "
        + "person's machine. Use Standard, Hsm or Byok for a secret the server encrypts.";

    public static bool IsSecret(Sdk.KeyValueVersionProtection protection)
    {
        return protection != Sdk.KeyValueVersionProtection.Public;
    }

    // The pair as the API returned it, plus ClientEncrypted derived from its level, so that a JWE document is never
    // taken for the value itself.
    public static JObject Describe(Sdk.KeyValue pair)
    {
        var content = (JObject)ToolResults.ToToken(pair);
        content[ClientEncryptedProperty] = pair.Protection == Sdk.KeyValueProtection.Hyok;
        return content;
    }

    // The pair without its value, a deleted pair included; null when the key does not exist.
    public static async Task<Sdk.KeyValue?> FindAsync(
        Sdk.IKeyValuesApiClient keyValues,
        string organization,
        string project,
        string view,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            return await keyValues.GetKeyValueAsync(organization, project, view, key, false, true, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return null;
        }
    }

    // A listing filter: validated names, comma-separated as the API takes them.
    public static string? ToFilter(IReadOnlyCollection<string>? protections, string parameter)
    {
        if (protections is null || protections.Count == 0)
        {
            return null;
        }

        return string.Join(",", protections.Select(protection => EnumNames.Parse<Sdk.KeyValueProtection>(protection, parameter)).Distinct());
    }
}
