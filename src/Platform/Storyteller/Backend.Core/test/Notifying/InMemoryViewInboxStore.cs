using _42.Platform.Storyteller.Notifying;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

// The store as Cosmos DB answers: an ETag that changes with every write, null for a taken id and for a lost replace,
// and a list without ETags. It expires nothing by itself; a test removes or hides an item where the expiry matters.
internal sealed class InMemoryViewInboxStore : IViewInboxStore
{
    private readonly Dictionary<string, ViewInboxRecord> _records = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private int _version;

    // Every stored write, with the time to live it was given.
    public List<(ViewInboxRecord Record, TimeSpan TimeToLive)> Writes { get; } = [];

    // The id of every point read.
    public List<string> Reads { get; } = [];

    // Every replace that was tried, the lost ones included.
    public List<ViewInboxRecord> Replaces { get; } = [];

    public List<(ViewInboxFilter Filter, IReadOnlyCollection<AccountRole> Roles, string? Before, int Take)> Queries { get; } = [];

    // Called before a create looks for the id, a read for the item and a replace at the ETag: what another caller or
    // the expiry does in between.
    public Action<ViewInboxRecord>? Creating { get; set; }

    public Action<string>? Reading { get; set; }

    public Action<ViewInboxRecord>? Replacing { get; set; }

    // Called with each stored record, while the caller waits for the write.
    public Action<ViewInboxRecord>? Saved { get; set; }

    // The stored item, read without a trace.
    public ViewInboxItem? Stored(string id)
    {
        return _records.Values.SingleOrDefault(record => record.Item.Id == id)?.Item;
    }

    // A write of another caller: the stored item gets a new ETag, and the change, so the next replace of a caller that
    // read it before is lost.
    public void TakeOver(string id, Func<ViewInboxItem, ViewInboxItem>? change = null)
    {
        var key = KeyOf(id);
        var stored = _records[key];
        _records[key] = stored with { Item = change is null ? stored.Item : change(stored.Item), ETag = NextETag() };
    }

    // The expiry: the item is gone.
    public void Remove(string id)
    {
        _records.Remove(KeyOf(id));
    }

    // An expired item as the vNext emulator keeps it for a while: no read finds it, and a create of its id conflicts.
    public void Hide(string id)
    {
        _hidden.Add(KeyOf(id));
    }

    public Task<ViewInboxRecord?> TryCreateAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Creating?.Invoke(record);

        var key = Key(record.Organization, record.Project, record.View, record.Item.Id);
        return Task.FromResult(_records.ContainsKey(key) ? null : Store(key, record, timeToLive));
    }

    public Task<ViewInboxRecord?> GetAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads.Add(id);
        Reading?.Invoke(id);

        var key = Key(organization, project, view, id);
        return Task.FromResult(_hidden.Contains(key) ? null : _records.GetValueOrDefault(key));
    }

    public Task<ViewInboxRecord?> TryReplaceAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (record.ETag is null)
        {
            throw new ArgumentException("A replace needs the ETag of a point read or of a write; the records of a query have none.", nameof(record));
        }

        Replaces.Add(record);
        Replacing?.Invoke(record);

        var key = Key(record.Organization, record.Project, record.View, record.Item.Id);
        var isCurrent = !_hidden.Contains(key) && _records.TryGetValue(key, out var stored) && stored.ETag == record.ETag;
        var saved = isCurrent ? Store(key, record, timeToLive) : null;

        // as a store over the network: a write that reached it stays, also when its caller went away before the answer
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(saved);
    }

    public Task<IReadOnlyList<ViewInboxRecord>> QueryAsync(
        string organization,
        string project,
        string view,
        ViewInboxFilter filter,
        IReadOnlyCollection<AccountRole> roles,
        string? before,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Add((filter, roles, before, take));

        IReadOnlyList<ViewInboxRecord> page = Visible(organization, project, view, roles)
            .Where(record => (record.Item.State == ViewInboxItemState.Open) == (filter == ViewInboxFilter.Open))
            .Where(record => before is null || string.CompareOrdinal(record.Item.Id, before) < 0)
            .OrderByDescending(record => record.Item.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(record => record with { ETag = null })
            .ToList();

        return Task.FromResult(page);
    }

    public Task<int> CountOpenAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AccountRole> roles,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Visible(organization, project, view, roles).Count(record => record.Item.State == ViewInboxItemState.Open));
    }

    private static string Key(string organization, string project, string view, string id) => $"{organization}|{project}|{view}|{id}";

    private IEnumerable<ViewInboxRecord> Visible(string organization, string project, string view, IReadOnlyCollection<AccountRole> roles)
    {
        return _records
            .Where(stored => !_hidden.Contains(stored.Key))
            .Select(stored => stored.Value)
            .Where(record => record.Organization == organization && record.Project == project && record.View == view)
            .Where(record => roles.Contains(record.Item.MinimalRole));
    }

    private ViewInboxRecord? Store(string key, ViewInboxRecord record, TimeSpan timeToLive)
    {
        var saved = record with { ETag = NextETag() };
        _records[key] = saved;
        Writes.Add((saved, timeToLive));
        Saved?.Invoke(saved);
        return saved;
    }

    private string KeyOf(string id) => _records.Keys.Single(key => key.EndsWith($"|{id}", StringComparison.Ordinal));

    private string NextETag() => $"\"{++_version}\"";
}
