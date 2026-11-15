using System.Threading;
using System.Threading.Tasks;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Services.Projects;

// What sform keeps of a project's settings between runs, so the console output of later commands shows them without a
// request (docs/Platform/Storyteller/specs/2026-11-15 Project Settings on the Server.md, section 7.3): the cache of
// ProjectSettingsProvider.
public interface IProjectSettingsCache
{
    // After a successful write of sform project settings set, edit or reset: the settings the API answered with and
    // their ETag, so the next command shows the new ones at once. Best effort: never throws.
    Task RememberAsync(string organization, string project, SdkSettings settings, string? etag, CancellationToken cancellationToken = default);
}
