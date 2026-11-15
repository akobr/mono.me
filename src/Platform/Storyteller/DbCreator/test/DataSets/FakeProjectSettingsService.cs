using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The settings of projects in memory, keyed "{organization}.{project}": every project has the defaults (version 0)
// until a write, except those in Missing, which do not exist. A write checks its condition and raises the version as
// the service does; OnSet runs before it (to change the settings meanwhile), SetFailure fails it.
internal sealed class FakeProjectSettingsService : IProjectSettingsService
{
    public Dictionary<string, ProjectSettings> Stored { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    public List<SettingsWrite> Writes { get; } = [];

    public Action? OnSet { get; set; }

    public StorytellerException? SetFailure { get; set; }

    public Task<ProjectSettings> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Get(organization, project));
    }

    public Task<ProjectSettingsWrite> SetAsync(
        string organization,
        string project,
        ProjectSettings settings,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default)
    {
        OnSet?.Invoke();
        Writes.Add(new SettingsWrite(organization, project, settings, condition, caller));

        if (SetFailure is not null)
        {
            throw SetFailure;
        }

        var stored = Get(organization, project);

        if (!condition.Holds(stored.Version))
        {
            throw new ProjectSettingsPreconditionFailedException(stored.Version);
        }

        if (ProjectSettingsRules.SameValues(stored, settings))
        {
            return Task.FromResult(new ProjectSettingsWrite(stored, false));
        }

        var written = settings with { Version = stored.Version + 1, Modified = AuditStamp.Of(caller.Actor, DateTimeOffset.UnixEpoch) };
        Stored[$"{organization}.{project}"] = written;
        return Task.FromResult(new ProjectSettingsWrite(written, true));
    }

    public Task<ProjectSettingsWrite> PatchAsync(
        string organization,
        string project,
        JsonObject patch,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("A seed never patches project settings.");
    }

    // Settings stored by someone else, at the given version.
    public void Store(string organization, string project, ProjectSettings settings, long version = 3)
    {
        Stored[$"{organization}.{project}"] = settings with { Version = version, Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch) };
    }

    private ProjectSettings Get(string organization, string project)
    {
        var key = $"{organization}.{project}";

        if (Missing.Contains(key))
        {
            throw new NotFoundException($"The project {key} does not exist.");
        }

        return Stored.GetValueOrDefault(key) ?? new ProjectSettings();
    }

    public sealed record class SettingsWrite(string Organization, string Project, ProjectSettings Settings, ProjectSettingsCondition Condition, KeyValueCaller Caller);
}
