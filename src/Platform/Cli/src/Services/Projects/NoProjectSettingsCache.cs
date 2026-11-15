using System.Threading;
using System.Threading.Tasks;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Services.Projects;

// Keeps nothing: until the console output follows the project settings, no command reads them back.
public sealed class NoProjectSettingsCache : IProjectSettingsCache
{
    public Task RememberAsync(string organization, string project, SdkSettings settings, string? etag, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
