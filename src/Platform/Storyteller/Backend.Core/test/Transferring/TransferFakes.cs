using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class InMemoryTransferStore : IConfigurationTransferStore
{
    private readonly Dictionary<string, ConfigurationTransferRecord> _records = new(StringComparer.Ordinal);
    private int _version;

    public List<ConfigurationTransferRecord> Saves { get; } = [];

    public int ActiveCount { get; set; }

    public Task<ConfigurationTransferRecord> CreateAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken = default)
    {
        var saved = record with { ETag = $"\"{++_version}\"" };
        _records.Add(Key(record.Organization, record.Project, record.View, record.Kind, record.Id), saved);
        Saves.Add(saved);
        return Task.FromResult(saved);
    }

    public Task<ConfigurationTransferRecord?> GetAsync(string organization, string project, string view, ConfigurationTransferKind kind, string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.GetValueOrDefault(Key(organization, project, view, kind, id)));
    }

    public Task<ConfigurationTransferRecord?> TryReplaceAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken = default)
    {
        var key = Key(record.Organization, record.Project, record.View, record.Kind, record.Id);

        if (!_records.TryGetValue(key, out var stored) || stored.ETag != record.ETag)
        {
            return Task.FromResult<ConfigurationTransferRecord?>(null);
        }

        var saved = record with { ETag = $"\"{++_version}\"" };
        _records[key] = saved;
        Saves.Add(saved);
        return Task.FromResult<ConfigurationTransferRecord?>(saved);
    }

    public Task<int> CountActiveAsync(string organization, string project, DateTimeOffset changedSince, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ActiveCount);
    }


    private static string Key(string organization, string project, string view, ConfigurationTransferKind kind, string id) => $"{organization}|{project}|{view}|{kind}|{id}";
}

internal sealed class InMemoryArchiveStorage(TimeProvider time) : IConfigurationArchiveStorage
{
    public Dictionary<string, (byte[] Content, DateTimeOffset Modified)> Archives { get; } = new(StringComparer.Ordinal);

    public async Task UploadAsync(string name, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Archives[name] = (buffer.ToArray(), time.GetUtcNow());
    }

    public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream?>(Archives.TryGetValue(name, out var archive) ? new MemoryStream(archive.Content, writable: false) : null);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Archives.Remove(name);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var old = Archives.Where(archive => archive.Value.Modified < threshold).Select(archive => archive.Key).ToList();
        old.ForEach(name => Archives.Remove(name));
        return Task.FromResult(old.Count);
    }
}

internal sealed class RecordingTransferQueue : IConfigurationTransferQueue
{
    public List<ConfigurationTransferMessage> Messages { get; } = [];

    public Exception? Failure { get; set; }

    public Task EnqueueAsync(ConfigurationTransferMessage message, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}

// Only the two members an export and an import use.
internal sealed class FakeConfigurationService : IConfigurationService
{
    public IReadOnlyList<ConfigurationHistory> Histories { get; set; } = [];

    public Func<FullKey, JObject, ConfigurationImportResult> Import { get; set; } = (_, _) => new ConfigurationImportResult(ConfigurationImportOutcome.Created, 1);

    public List<(FullKey Key, JObject Content, AuditActor Actor, bool Force, bool DryRun)> Imports { get; } = [];

    public Task<IReadOnlyList<ConfigurationHistory>> GetConfigurationHistoriesAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Histories);
    }

    public Task<ConfigurationImportResult> ImportConfigurationAsync(FullKey key, JObject content, AuditActor actor, bool force = false, bool dryRun = false)
    {
        Imports.Add((key, content, actor, force, dryRun));
        return Task.FromResult(Import(key, content));
    }

    public Task<bool> HasConfigurationContentAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetRawConfigurationAsync(FullKey key) => throw new NotSupportedException();

    public Task<ConfigurationsResponse> ListConfigurationsAsync(string organization, string project, string view, string? annotationType = null, string? keyPrefix = null, string? continuationToken = null) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationWithSecretsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force = false) => throw new NotSupportedException();

    public Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false) => throw new NotSupportedException();

    public Task ClearConfigurationAsync(FullKey key, AuditActor? actor = null) => throw new NotSupportedException();

    public Task DeleteAsync(FullKey key, AuditActor? actor = null) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView) => throw new NotSupportedException();
}
