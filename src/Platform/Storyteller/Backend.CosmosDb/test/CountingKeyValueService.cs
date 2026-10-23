#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

/// <summary>
/// Counts the lookups of the binding sources, to check that one resolved read looks a key up once.
/// </summary>
internal sealed class CountingKeyValueService(CosmosKeyValueService inner) : IKeyValueService
{
    private readonly ConcurrentDictionary<string, int> _lookups = new(StringComparer.OrdinalIgnoreCase);

    public int GetLookupCount(string key)
    {
        return _lookups.GetValueOrDefault(key);
    }

    public void ResetLookups()
    {
        _lookups.Clear();
    }

    public Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request)
        => inner.ListAsync(organization, project, view, request);

    public Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller)
        => inner.GetAsync(organization, project, view, key, options, caller);

    public Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller)
        => inner.SetAsync(organization, project, view, key, write, caller);

    public Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller)
        => inner.DeleteAsync(organization, project, view, key, options, caller);

    public Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => inner.RestoreAsync(organization, project, view, request, caller);

    public Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => inner.PurgeAsync(organization, project, view, request, caller);

    public Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => inner.ProtectAsync(organization, project, view, request, caller);

    public Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key)
        => inner.GetVersionsAsync(organization, project, view, key);

    public Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller)
        => inner.GetVersionAsync(organization, project, view, key, version, reveal, caller);

    public Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller)
        => inner.GetVersionChangesAsync(organization, project, view, key, fromVersion, toVersion, caller);

    public Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller)
        => inner.ExportAsync(organization, project, view, request, caller);

    public Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller)
        => inner.ImportAsync(organization, project, view, document, options, caller);

    public Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets)
    {
        _lookups.AddOrUpdate(key, 1, (_, count) => count + 1);
        return inner.LookupAsync(organization, project, view, key, includeSecrets);
    }

    public Task<MarkedRead<KeyValueLookup>> LookupWithMarkerAsync(string organization, string project, string view, string key, bool includeSecrets)
    {
        _lookups.AddOrUpdate(key, 1, (_, count) => count + 1);
        return inner.LookupWithMarkerAsync(organization, project, view, key, includeSecrets);
    }
}
