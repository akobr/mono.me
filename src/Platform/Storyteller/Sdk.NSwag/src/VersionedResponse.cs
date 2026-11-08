using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.Sdk;

// The answer of an annotation read or write with the version it carries: the body, and its ETag to send back in
// If-Match (a write of only that version) or If-None-Match.
public sealed class VersionedResponse<TResult>
    where TResult : class
{
    private const string ETagHeader = "ETag";

    public VersionedResponse(int statusCode, TResult result, IReadOnlyDictionary<string, IEnumerable<string>> headers)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(headers);

        StatusCode = statusCode;
        Result = result;

        // A copy with case-insensitive names, whatever the caller passed.
        Headers = headers.ToDictionary(header => header.Key, header => (IEnumerable<string>)header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        ETag = Headers.TryGetValue(ETagHeader, out var values) && string.Join(", ", values) is { Length: > 0 } tag ? tag : null;
    }

    public int StatusCode { get; }

    public TResult Result { get; }

    // The raw value, to send back as it is; null when the answer has none (a deleted annotation, a PUT).
    public string? ETag { get; }

    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; }
}
