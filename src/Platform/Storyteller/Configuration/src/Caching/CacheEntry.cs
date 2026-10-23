namespace _42.Platform.Storyteller.Sdk.Configuring.Caching;

// A last-known-good document: the server's body before any local resolution, so a resolved secret never reaches the
// disk.
internal sealed record CacheEntry(string Identity, string? ETag, DateTimeOffset StoredAt, string? CacheControl, Configuration Document, bool IsProtected)
{
    // A plain entry is not authenticated: whoever can write the directory could keep its tag and change its document,
    // and a 304 would then confirm the changed document. Only a protected entry's tag is sent.
    public string? TrustedETag => IsProtected ? ETag : null;
}
