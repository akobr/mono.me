using System;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Credentials never cross a network in clear text: an absolute https URL without user info, or http to localhost or a
// loopback address literal, never to a name that DNS resolves. Public, so the provider and sform apply the same rule.
public static class StorytellerUrlRule
{
    private static readonly char[] AuthorityEnd = ['/', '?', '#', '\\'];

    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttps || IsLocalHttp(uri));
    }

    public static bool IsLocalHttp(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                || string.Equals(ReadWrittenHost(uri), "localhost", StringComparison.OrdinalIgnoreCase));
    }

    // The source is a variable or an option, printed in the message; the value is not, as it may carry user info. The
    // Uri keeps the text as written (OriginalString). A refused URL is a StorytellerAuthenticationException (Configuration).
    public static Uri Require(string value, string source)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? Require(uri, source)
            : throw Invalid(source);
    }

    public static Uri Require(Uri uri, string source)
    {
        return IsAllowed(uri) ? uri : throw Invalid(source);
    }

    // The API base: paths are appended to it, so it has no query or fragment either, as the provider requires.
    public static Uri RequireEndpoint(string value, string source)
    {
        return RequireEndpoint(Require(value, source), source);
    }

    public static Uri RequireEndpoint(Uri uri, string source)
    {
        Require(uri, source);

        return uri.Query.Length == 0 && uri.Fragment.Length == 0
            ? uri
            : throw StorytellerAuthenticationException.Configuration($"{source} must not have a query or a fragment; set the API base, /api included.");
    }

    // For messages, ToString() and identities: scheme, host, port and path, never user info, a query or a fragment. A
    // relative URL, which no check has refused yet, has none of them to print.
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

    // A base URL to append paths to, without its trailing slash.
    internal static string TrimBase(Uri uri)
    {
        return uri.AbsoluteUri.TrimEnd('/');
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

    private static StorytellerAuthenticationException Invalid(string source)
    {
        return StorytellerAuthenticationException.Configuration(
            $"{source} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.");
    }
}
