using System.Text.Json;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.KeyValues;

// A reference is a public pair whose value names a secret in the customer's own vault. The platform checks the
// shape and never resolves it.
public static partial class SecretReferences
{
    public const string Shorthand = "kv://";

    public static bool IsReferenceContentType(string? contentType)
    {
        return KeyValueContentTypes.IsReference(contentType);
    }

    // The secret URI of a valid reference value, or an InvalidInputException.
    public static Uri Ensure(string value, string contentType)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();

        if (string.Equals(contentType, KeyValueContentTypes.AppConfigurationKeyVaultReference, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("uri", out var uri)
                    || uri.ValueKind != JsonValueKind.String)
                {
                    throw Invalid("an App Configuration reference is a JSON object with a 'uri'");
                }

                text = uri.GetString()!.Trim();
            }
            catch (JsonException)
            {
                throw Invalid("an App Configuration reference is a JSON object with a 'uri'");
            }
        }
        else if (text.StartsWith(Shorthand, StringComparison.OrdinalIgnoreCase))
        {
            // kv://{vault}/{name}[/{version}]
            var match = ShorthandPattern().Match(text);

            if (!match.Success)
            {
                throw Invalid("the shorthand is kv://{vault}/{name}[/{version}]");
            }

            text = $"https://{match.Groups["vault"].Value}.vault.azure.net/secrets/{match.Groups["name"].Value}"
                + (match.Groups["version"].Success ? $"/{match.Groups["version"].Value}" : string.Empty);
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var result)
            || !string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !SecretPathPattern().IsMatch(result.AbsolutePath))
        {
            throw Invalid("the value must be https://{vault}/secrets/{name}[/{version}] or kv://{vault}/{name}[/{version}]");
        }

        return result;
    }

    private static InvalidInputException Invalid(string reason)
    {
        return new InvalidInputException($"The secret reference is not valid: {reason}.", ErrorCodes.InvalidSecretReference);
    }

    [GeneratedRegex("^kv://(?<vault>[A-Za-z0-9-]{3,24})/(?<name>[A-Za-z0-9-]{1,127})(/(?<version>[0-9a-fA-F]{32}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShorthandPattern();

    [GeneratedRegex("^/secrets/[A-Za-z0-9-]{1,127}(/[0-9a-fA-F]{32})?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPathPattern();
}
