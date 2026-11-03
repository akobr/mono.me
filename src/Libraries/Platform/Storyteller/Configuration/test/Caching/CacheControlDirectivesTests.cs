using _42.Platform.Storyteller.Sdk.Configuring.Caching;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Caching;

public class CacheControlDirectivesTests
{
    [Theory]
    [InlineData("private, no-cache", StorytellerReadMode.Resolved, true)]
    [InlineData("private, no-cache", StorytellerReadMode.Effective, true)]
    [InlineData("no-store", StorytellerReadMode.Resolved, false)]
    [InlineData("no-store", StorytellerReadMode.Effective, false)]
    [InlineData("private, No-Store", StorytellerReadMode.Effective, false)]
    [InlineData("no-store=\"x\"", StorytellerReadMode.Effective, false)]
    [InlineData("no-storefront", StorytellerReadMode.Effective, true)]
    [InlineData(null, StorytellerReadMode.Resolved, false)]
    [InlineData(null, StorytellerReadMode.Effective, true)]
    public void APlainEntry(string? cacheControl, StorytellerReadMode mode, bool allowed)
    {
        CacheControlDirectives.AllowsPlainEntry(mode, cacheControl).ShouldBe(allowed);
    }
}
