namespace _42.Platform.Storyteller.Sdk.Configuring;

// Where the applied document came from.
public enum StorytellerConfigurationOrigin
{
    // A 200, or a 304 that confirmed the document the provider holds.
    Platform = 0,

    // The last-known-good cache, after a transient failure at startup.
    Cache,

    // Nothing: an optional provider that started without the platform and without a usable cache entry.
    Empty,
}
