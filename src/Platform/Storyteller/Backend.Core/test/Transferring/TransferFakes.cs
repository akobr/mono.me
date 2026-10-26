using System.Globalization;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class InMemoryTransferStore : IViewTransferStore
{
    private readonly Dictionary<string, ViewTransferRecord> _records = new(StringComparer.Ordinal);
    private int _version;

    public List<ViewTransferRecord> Saves { get; } = [];

    public int ActiveCount { get; set; }

    public Task<ViewTransferRecord> CreateAsync(ViewTransferRecord record, CancellationToken cancellationToken = default)
    {
        var saved = record with { ETag = $"\"{++_version}\"" };
        _records.Add(Key(record.Organization, record.Project, record.View, record.Kind, record.Id), saved);
        Saves.Add(saved);
        return Task.FromResult(saved);
    }

    public Task<ViewTransferRecord?> GetAsync(string organization, string project, string view, ViewTransferKind kind, string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.GetValueOrDefault(Key(organization, project, view, kind, id)));
    }

    public Task<ViewTransferRecord?> TryReplaceAsync(ViewTransferRecord record, CancellationToken cancellationToken = default)
    {
        var key = Key(record.Organization, record.Project, record.View, record.Kind, record.Id);

        if (!_records.TryGetValue(key, out var stored) || stored.ETag != record.ETag)
        {
            return Task.FromResult<ViewTransferRecord?>(null);
        }

        var saved = record with { ETag = $"\"{++_version}\"" };
        _records[key] = saved;
        Saves.Add(saved);
        return Task.FromResult<ViewTransferRecord?>(saved);
    }

    public (DateTimeOffset QueuedSince, DateTimeOffset RunningSince)? CountedSince { get; private set; }

    public Task<int> CountActiveAsync(
        string organization,
        string project,
        DateTimeOffset queuedSince,
        DateTimeOffset runningSince,
        CancellationToken cancellationToken = default)
    {
        CountedSince = (queuedSince, runningSince);
        return Task.FromResult(ActiveCount);
    }

    private static string Key(string organization, string project, string view, ViewTransferKind kind, string id) => $"{organization}|{project}|{view}|{kind}|{id}";
}

internal sealed class InMemoryArchiveStorage(TimeProvider time) : IViewArchiveStorage
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

internal sealed class RecordingTransferQueue : IViewTransferQueue
{
    public List<ViewTransferMessage> Messages { get; } = [];

    public Exception? Failure { get; set; }

    public Task EnqueueAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}

// Only the members an export and an import use.
internal sealed class FakeConfigurationService(ExportReads? reads = null, ImportLog? imports = null) : IConfigurationService
{
    public IReadOnlyList<ConfigurationHistory> Histories { get; set; } = [];

    public Func<FullKey, JObject, ViewImportResult> Import { get; set; } = (_, _) => new ViewImportResult(ViewImportOutcome.Created, 1);

    public List<(FullKey Key, JObject Content, ViewImportOptions Options, ViewImportOverlay Overlay)> Imports { get; } = [];

    // The stored configurations of the listing: the key and whether its own document has content.
    public IReadOnlyList<(string Key, bool HasContent)> Listed { get; set; } = [];

    // Configurations per page of the listing.
    public int PageSize { get; set; } = 1000;

    // The continuation token of each listing request.
    public List<string?> ListReads { get; } = [];

    public Task<IReadOnlyList<ConfigurationHistory>> GetConfigurationHistoriesAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        reads?.Add(ViewArchiveContent.Configurations, organization, project, view);
        return Task.FromResult(Histories);
    }

    public Task<ViewImportResult> ImportConfigurationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay)
    {
        Imports.Add((key, content, options, overlay));
        imports?.Add(ViewDocumentKind.Configuration, null, key.Annotation.ToString(), options, overlay);
        return Task.FromResult(Import(key, content));
    }

    public Task<ConfigurationsResponse> ListConfigurationsAsync(string organization, string project, string view, string? annotationType = null, string? keyPrefix = null, string? continuationToken = null)
    {
        ListReads.Add(continuationToken);
        var skip = continuationToken is null ? 0 : int.Parse(continuationToken, CultureInfo.InvariantCulture);
        var page = Listed
            .Skip(skip)
            .Take(PageSize)
            .Select(listed => new ConfigurationSummary
            {
                AnnotationKey = listed.Key,
                AnnotationType = AnnotationKey.Parse(listed.Key).Type,
                Version = 1,
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch),
                HasContent = listed.HasContent,
            })
            .ToList();
        var next = skip + page.Count;

        return Task.FromResult(new ConfigurationsResponse
        {
            Configurations = page,
            ContinuationToken = next < Listed.Count ? next.ToString(CultureInfo.InvariantCulture) : null,
            Count = page.Count,
        });
    }

    public Task<bool> HasConfigurationContentAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetRawConfigurationAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationWithSecretsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key) => throw new NotSupportedException();

    public Task<ConfigurationComposition?> GetConfigurationCompositionAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force = false) => throw new NotSupportedException();

    public Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false) => throw new NotSupportedException();

    public Task<bool> ClearConfigurationAsync(FullKey key, AuditActor? actor = null) => throw new NotSupportedException();

    public Task DeleteAsync(FullKey key, AuditActor? actor = null) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key) => throw new NotSupportedException();

    public Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion) => throw new NotSupportedException();

    public Task<DiffResult> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView) => throw new NotSupportedException();

    public bool HasBindings(Configuration effective) => throw new NotSupportedException();

    public Task<Configuration> ResolveConfigurationAsync(FullKey key, Configuration effective, bool includeSecrets = false, BindingDependencies? dependencies = null) => throw new NotSupportedException();

    public Task<bool> AreDependenciesUnchangedAsync(FullKey key, IReadOnlyList<BindingDependency> dependencies) => throw new NotSupportedException();
}
