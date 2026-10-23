using _42.Platform.Storyteller.Configuring;

namespace _42.Platform.Storyteller.KeyValues;

public interface IKeyValueService
{
    Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request);

    Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller);

    Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller);

    // False when the key does not exist (or is already soft-deleted and the delete is not permanent).
    Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller);

    Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key);

    Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller);

    Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller);

    Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller);

    Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller);

    // For the binding sources: the current value, or null when missing, deleted, or a secret without includeSecrets.
    Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets);

    // LookupAsync with the storage marker of the pair (see MarkedRead): the version of its item also when the value is
    // null because the pair is deleted or a secret without includeSecrets. The default cannot tell the marker.
    async Task<MarkedRead<KeyValueLookup>> LookupWithMarkerAsync(string organization, string project, string view, string key, bool includeSecrets)
    {
        return new MarkedRead<KeyValueLookup>(await LookupAsync(organization, project, view, key, includeSecrets), Marker: null);
    }
}
