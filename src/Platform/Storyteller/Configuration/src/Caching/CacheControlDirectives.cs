namespace _42.Platform.Storyteller.Sdk.Configuring.Caching;

internal static class CacheControlDirectives
{
    public static bool HasNoStore(string? cacheControl)
    {
        return cacheControl?.Split(',').Any(directive =>
            string.Equals(directive.Split('=', 2)[0].Trim(), "no-store", StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    // A plain entry needs a Cache-Control without no-store. In Resolved mode a missing header counts as no-store: the
    // server always sends one of its two values, so its absence means something in between changed it.
    public static bool AllowsPlainEntry(StorytellerReadMode mode, string? cacheControl)
    {
        if (cacheControl is null)
        {
            return mode != StorytellerReadMode.Resolved;
        }

        return !HasNoStore(cacheControl);
    }
}
