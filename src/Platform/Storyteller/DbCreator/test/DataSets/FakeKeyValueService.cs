using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The import and the listing of one values store in memory, as CosmosKeyValueService plans them: a missing or deleted key
// is Create, the same level, content type and value Unchanged, anything else Update. It refuses the keys it is told to
// (KeyValueImportInvalidException, nothing written), fails the writes of others, and records every call.
internal sealed class FakeKeyValueService : IKeyValueService
{
    public Dictionary<string, StoredPair> Pairs { get; } = new(StringComparer.Ordinal);

    // keys whose items the validation refuses, with ProtectionNotAllowed
    public HashSet<string> RefusedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    // keys whose writes fail, with KeyValueBusy
    public HashSet<string> FailingWriteKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    // thrown by the next write instead of writing
    public Exception? WriteException { get; set; }

    public List<ImportCall> Imports { get; } = [];

    // Called at each import, before it plans, so a test can see what ran before it.
    public Action? OnImport { get; set; }

    public int Lists { get; private set; }

    // the items per page of a listing, so a test sees the continuation
    public int PageSize { get; set; } = 2;

    public void Add(string key, KeyValueProtection protection, string value, string? contentType = null, bool isDeleted = false)
    {
        Pairs[KeyValueRules.Normalize(key)] = new StoredPair(new KeyValueDocumentItem { Key = key, Protection = protection, Value = value, ContentType = contentType }, isDeleted, 1);
    }

    public Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller)
    {
        Imports.Add(new ImportCall(organization, project, view, document.Items.Select(item => item.Key).ToList(), options, caller));
        OnImport?.Invoke();
        var refused = document.Items.Where(item => RefusedKeys.Contains(item.Key)).ToList();

        if (refused.Count > 0)
        {
            throw new KeyValueImportInvalidException(
                $"{refused.Count} item(s) of the import are not valid; nothing was written.",
                refused.Select(item => new KeyValueImportItemResult
                {
                    Key = item.Key,
                    Protection = item.Protection,
                    Action = KeyValueImportAction.Unchanged,
                    ErrorCode = ErrorCodes.ProtectionNotAllowed,
                    Message = $"The level {item.Protection} is not allowed.",
                }).ToList());
        }

        if (!options.DryRun && WriteException is { } exception)
        {
            WriteException = null;
            throw exception;
        }

        var results = new List<KeyValueImportItemResult>();

        foreach (var item in document.Items)
        {
            var key = KeyValueRules.Normalize(item.Key);
            Pairs.TryGetValue(key, out var existing);
            var action = existing is null || existing.IsDeleted
                ? KeyValueImportAction.Create
                : existing.Item.Protection == item.Protection && existing.Item.ContentType == item.ContentType && existing.Item.Value == item.Value
                    ? KeyValueImportAction.Unchanged
                    : KeyValueImportAction.Update;

            if (!options.DryRun && FailingWriteKeys.Contains(item.Key))
            {
                results.Add(new KeyValueImportItemResult { Key = item.Key, Protection = item.Protection, Action = action, ErrorCode = ErrorCodes.KeyValueBusy, Message = "Busy." });
                continue;
            }

            if (!options.DryRun && action != KeyValueImportAction.Unchanged)
            {
                Pairs[key] = new StoredPair(item, IsDeleted: false, (existing?.Version ?? 0) + 1);
            }

            results.Add(new KeyValueImportItemResult { Key = item.Key, Protection = item.Protection, Action = action, Version = existing?.Version });
        }

        return Task.FromResult(new KeyValueImportResult { IsDryRun = options.DryRun, IsComplete = results.All(result => result.ErrorCode is null), Items = results });
    }

    public Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request)
    {
        Lists++;
        request.State.ShouldBe(KeyValueState.Deleted);
        var skip = request.ContinuationToken is null ? 0 : int.Parse(request.ContinuationToken, System.Globalization.CultureInfo.InvariantCulture);
        var deleted = Pairs.Values.Where(pair => pair.IsDeleted).OrderBy(pair => pair.Item.Key, StringComparer.Ordinal).ToList();
        var page = deleted.Skip(skip).Take(PageSize).ToList();

        return Task.FromResult(new KeyValuesResponse
        {
            Items = page.Select(pair => new KeyValueSummary
            {
                Key = pair.Item.Key,
                Protection = pair.Item.Protection,
                Version = pair.Version,
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch),
                IsDeleted = true,
            }).ToList(),
            ContinuationToken = skip + PageSize < deleted.Count ? (skip + PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            Count = page.Count,
        });
    }

    public Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key) => throw new NotSupportedException();

    public Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller) => throw new NotSupportedException();

    public Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets) => throw new NotSupportedException();

    public sealed record class StoredPair(KeyValueDocumentItem Item, bool IsDeleted, ulong Version);

    public sealed record class ImportCall(string Organization, string Project, string View, IReadOnlyList<string> Keys, KeyValueImportOptions Options, KeyValueCaller Caller);
}
