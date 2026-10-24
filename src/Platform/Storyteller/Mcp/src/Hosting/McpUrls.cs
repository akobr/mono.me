namespace _42.Platform.Storyteller.Mcp.Hosting;

// An absolute HTTP(S) URL without a trailing slash; HTTPS outside Development, where tokens travel.
internal static class McpUrls
{
    public static bool IsValid(string? value, IHostEnvironment environment, out string problem)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            problem = "must be an absolute HTTP(S) URL.";
            return false;
        }

        if (value!.EndsWith('/'))
        {
            problem = "must not end with a slash.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !environment.IsDevelopment())
        {
            problem = "must use HTTPS outside Development.";
            return false;
        }

        problem = string.Empty;
        return true;
    }
}
