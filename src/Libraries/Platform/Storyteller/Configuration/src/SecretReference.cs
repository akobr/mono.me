using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// A secret reference in one of the platform's own forms, for resolvers and diagnostics. It names a secret and holds
// none. Parsed strictly: the whole trimmed value must match, so a reference inside a longer string is not one. Follows
// the server's check of a reference pair, and is stricter about the parts of a URI: no user info, port, query or
// fragment, and a host that is a DNS name.
public sealed partial class SecretReference
{
    private const string ShorthandScheme = "kv://";
    private const string HttpsScheme = "https://";
    private const string UriProperty = "uri";

    private SecretReference(string original, SecretReferenceForm form, string? vaultName, Uri? vaultUri, string name, string? version)
    {
        Original = original;
        Form = form;
        VaultName = vaultName;
        VaultUri = vaultUri;
        Name = name;
        Version = version;
    }

    // The value as it was given.
    public string Original { get; }

    public SecretReferenceForm Form { get; }

    // The vault of the shorthand; null for the URI forms, whose vault is the host of VaultUri.
    public string? VaultName { get; }

    // https://{host}/ of the URI forms; null for the shorthand, whose host the resolver chooses.
    public Uri? VaultUri { get; }

    public string Name { get; }

    // 32 hex characters, as written; null for the latest version.
    public string? Version { get; }

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;

        if (value is null)
        {
            return false;
        }

        var text = value.AsSpan().Trim();

        if (text.StartsWith(ShorthandScheme, StringComparison.Ordinal))
        {
            var match = ShorthandPattern().Match(text.ToString());

            if (!match.Success)
            {
                return false;
            }

            reference = new SecretReference(
                value,
                SecretReferenceForm.Shorthand,
                match.Groups["vault"].Value,
                null,
                match.Groups["name"].Value,
                match.Groups["version"].Success ? match.Groups["version"].Value : null);
            return true;
        }

        if (text.StartsWith(HttpsScheme, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseSecretUri(value, text.ToString(), SecretReferenceForm.SecretUri, out reference);
        }

        if (text.StartsWith('{') && text.EndsWith('}') && TryReadUri(text.ToString(), out var uri))
        {
            return TryParseSecretUri(value, uri.Trim(), SecretReferenceForm.AppConfiguration, out reference);
        }

        return false;
    }

    // A reference names a secret, so its text is safe to show.
    public override string ToString()
    {
        return Original;
    }

    private static bool TryParseSecretUri(string original, string text, SecretReferenceForm form, [NotNullWhen(true)] out SecretReference? reference)
    {
        reference = null;
        var match = SecretUriPattern().Match(text);

        if (!match.Success)
        {
            return false;
        }

        // The pattern keeps user info, a port, IPv6 literals and escapes out of the host; the host must also be a DNS name
        // with no empty label, so a trailing dot cannot slip past a suffix check.
        var host = match.Groups["host"].Value;

        if (host.StartsWith('.')
            || host.EndsWith('.')
            || Uri.CheckHostName(host) != UriHostNameType.Dns
            || !Uri.TryCreate($"{HttpsScheme}{host}/", UriKind.Absolute, out var vaultUri)
            || vaultUri.HostNameType != UriHostNameType.Dns
            || !vaultUri.IsDefaultPort
            || !HasDnsIdnHost(vaultUri))
        {
            return false;
        }

        reference = new SecretReference(
            original,
            form,
            null,
            vaultUri,
            match.Groups["name"].Value,
            match.Groups["version"].Success ? match.Groups["version"].Value : null);
        return true;
    }

    // The ASCII form is what a resolver compares and connects to. A Unicode host can have none (a fullwidth '／', '＠' or
    // '：' maps to a delimiter), or one whose Unicode full stops ('。', '．') map to an empty label or a trailing dot.
    private static bool HasDnsIdnHost(Uri vaultUri)
    {
        string idnHost;

        try
        {
            idnHost = vaultUri.IdnHost;
        }
        catch (UriFormatException)
        {
            return false;
        }

        return Uri.CheckHostName(idnHost) == UriHostNameType.Dns
            && !idnHost.Split('.').Any(string.IsNullOrEmpty);
    }

    // A JSON object with one string property uri; other properties are ignored, as the server does.
    private static bool TryReadUri(string text, [NotNullWhen(true)] out string? uri)
    {
        uri = null;

        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals(UriProperty))
                {
                    continue;
                }

                if (uri is not null || property.Value.ValueKind != JsonValueKind.String)
                {
                    uri = null;
                    return false;
                }

                uri = property.Value.GetString()!;
            }

            return uri is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("^kv://(?<vault>[A-Za-z0-9-]{3,24})/(?<name>[A-Za-z0-9-]{1,127})(/(?<version>[0-9a-fA-F]{32}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShorthandPattern();

    [GeneratedRegex("^(?i:https)://(?<host>[^/?#@:\\[\\]%\\\\\\s]+)/secrets/(?<name>[A-Za-z0-9-]{1,127})(/(?<version>[0-9a-fA-F]{32}))?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretUriPattern();
}
