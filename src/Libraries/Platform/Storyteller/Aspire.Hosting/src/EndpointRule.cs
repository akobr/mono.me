namespace _42.Platform.Storyteller.AspireHosting;

// The SDK's StorytellerUrlRule for the API base, copied so an AppHost does not reference the SDK: an absolute https URL
// without user info, a query or a fragment, or http to localhost or a loopback address literal, never to a name that
// DNS resolves. The process checks again; this fails the AppHost at the line that set a URL no process accepts.
internal static class EndpointRule
{
    private static readonly char[] AuthorityEnd = ['/', '?', '#', '\\'];

    public static bool IsAllowed(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttps || IsLocalHttp(uri))
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0;
    }

    // The message names the parameter, never the value, as it may carry user info.
    public static void Require(string value, string parameterName)
    {
        if (!IsAllowed(value))
        {
            throw new ArgumentException(
                "The Storyteller endpoint must be an absolute https URL without user info, a query or a fragment; http is allowed only for localhost or a loopback address. Set the API base, /api included.",
                parameterName);
        }
    }

    // localhost or a loopback address: the AppHost's machine to a process that runs there, but the container itself to
    // a container, and the deployed process itself once published.
    public static bool IsLoopback(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    private static bool IsLocalHttp(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                || string.Equals(ReadWrittenHost(uri), "localhost", StringComparison.OrdinalIgnoreCase));
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
