using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.Sdk;

// The answer of a conditional read: the body of a 200, or nothing for a 304, with the headers that drive caching.
public sealed class ConditionalResponse<TResult>
    where TResult : class
{
    private const string ETagHeader = "ETag";
    private const string CacheControlHeader = "Cache-Control";
    private const string SecretsWithheldHeader = "X-2S-Secrets-Withheld";

    public ConditionalResponse(int statusCode, TResult? result, IReadOnlyDictionary<string, IEnumerable<string>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        StatusCode = statusCode;
        Result = result;

        // A copy with case-insensitive names, whatever the caller passed.
        Headers = headers.ToDictionary(header => header.Key, header => (IEnumerable<string>)header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        ETag = GetHeader(ETagHeader);
        CacheControl = GetHeader(CacheControlHeader);
        IsNoStore = CacheControl?.Split(',').Any(IsNoStoreDirective) ?? false;
        SecretsWithheld = GetHeader(SecretsWithheldHeader);
    }

    // 200 or 304.
    public int StatusCode { get; }

    public bool IsNotModified => StatusCode == 304;

    // The body of a 200; null on a 304.
    public TResult? Result { get; }

    // The raw value, weak prefix included, to send back in If-None-Match as it is.
    public string? ETag { get; }

    public string? CacheControl { get; }

    public bool IsNoStore { get; }

    public string? SecretsWithheld { get; }

    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; }

    private static bool IsNoStoreDirective(string directive)
    {
        var name = directive.Split('=', 2)[0].Trim();
        return string.Equals(name, "no-store", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetHeader(string name)
    {
        return Headers.TryGetValue(name, out var values) && string.Join(", ", values) is { Length: > 0 } value
            ? value
            : null;
    }
}
