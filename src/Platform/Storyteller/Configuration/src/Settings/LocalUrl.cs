using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// A copy of the local URL rule of the SDK credentials (Sdk.NSwag/src/Credentials/LocalUrlRule.cs, internal there): an
// absolute https URL without user info, or http to localhost or a loopback address literal, never to a name that DNS
// resolves. A change there comes here too; SettingsReaderTests pins that both accept the same endpoints.
internal static class LocalUrl
{
    private static readonly char[] AuthorityEnd = ['/', '?', '#', '\\'];

    public static bool IsAllowed(Uri uri)
    {
        return uri.IsAbsoluteUri
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttps || IsLocalHttp(uri));
    }

    public static bool IsLocalHttp(Uri uri)
    {
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                || string.Equals(ReadWrittenHost(uri), "localhost", StringComparison.OrdinalIgnoreCase));
    }

    // Scheme, host, port and path, for messages and identities: never user info, a query or a fragment.
    [return: NotNullIfNotNull(nameof(uri))]
    public static string? Describe(Uri? uri)
    {
        return uri switch
        {
            null => null,
            { IsAbsoluteUri: true } => uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
            _ => "<relative>",
        };
    }

    // The host as written: Uri reports the name "loopback" as "localhost".
    private static string ReadWrittenHost(Uri uri)
    {
        var text = uri.OriginalString;
        var start = text.IndexOf("://", StringComparison.Ordinal);

        if (start < 0)
        {
            return string.Empty;
        }

        start += 3;
        var end = text.IndexOfAny(AuthorityEnd, start);
        var authority = end < 0 ? text[start..] : text[start..end];
        var port = authority.LastIndexOf(':');

        return port < 0 ? authority : authority[..port];
    }
}
