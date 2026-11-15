using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Services.Projects;

// project-settings.cache.json in the state directory (SFORM_HOME, else the folder of the assembly): the settings each
// project of each API answered last, with their ETag and when they were read, for the console output of later runs.
internal sealed record ProjectSettingsCacheFile
{
    // The entries of the projects read last are kept; older ones go.
    public const int MaxEntries = 50;

    [JsonPropertyName("entries")]
    public List<ProjectSettingsCacheEntry> Entries { get; init; } = [];

    public ProjectSettingsCacheEntry? Find(string baseUrl, string project)
    {
        return Entries.FirstOrDefault(entry => entry.Matches(baseUrl, project));
    }

    // The file with the entry in place of the one of its project, first, and at most MaxEntries.
    public ProjectSettingsCacheFile With(ProjectSettingsCacheEntry entry)
    {
        return new ProjectSettingsCacheFile
        {
            Entries = [entry, .. Entries.Where(other => other is not null && !other.Matches(entry.BaseUrl, entry.Project)).Take(MaxEntries - 1)],
        };
    }
}
