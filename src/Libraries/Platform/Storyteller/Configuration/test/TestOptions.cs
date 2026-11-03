using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.Sdk.Resilience;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Options over a fake environment, a mock file system and a stub handler, without retry delays.
internal static class TestOptions
{
    public static StorytellerConfigurationOptions Create(
        IDictionary<string, string?> environment,
        HttpMessageHandler? primaryHandler = null,
        MockFileSystem? fileSystem = null)
    {
        return new StorytellerConfigurationOptions
        {
            GetEnvironmentVariable = name => environment.TryGetValue(name, out var value) ? value : null,
            PrimaryHandler = primaryHandler,
            FileSystem = fileSystem ?? new UnixModeMockFileSystem(),
            Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero },
        };
    }
}
