using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.KeyValues;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

// Revalidates the inputs recorded by a resolve from their storage versions, in one ReadMany over the organization
// container: the ETag of each item in any state, or absent without one, the rule of the reads that recorded them
// (CosmosKeyValueService.LookupWithMarkerAsync, CosmosAnnotationService.GetAnnotationWithMarkerAsync). It reads no
// value and calls no service, so nothing is decrypted, metered or audited. Any doubt is "changed": the read then
// resolves again, it never answers a stale 304.
internal sealed class BindingDependencyValidator
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly ILogger _logger;

    public BindingDependencyValidator(IContainerRepositoryProvider repositoryProvider, ILogger logger)
    {
        _repositoryProvider = repositoryProvider;
        _logger = logger;
    }

    public async Task<bool> AreUnchangedAsync(FullKey key, IReadOnlyList<BindingDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(dependencies);

        var expected = new List<(string Id, string PartitionKey, string Marker)>(dependencies.Count);

        foreach (var dependency in dependencies)
        {
            switch (dependency.Kind)
            {
                case BindingDependencyKinds.KeyValue when KeyValueRules.IsValidKey(dependency.Key):
                    expected.Add((
                        CosmosKeyValueService.GetId(KeyValueRules.Normalize(dependency.Key)),
                        PartitionKeys.GetKeyValues(key.ProjectName, dependency.Source),
                        dependency.Marker));
                    break;

                // an invalid key is never stored, so its lookup reads nothing
                case BindingDependencyKinds.KeyValue:
                    if (!string.Equals(dependency.Marker, BindingDependency.Absent, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    break;

                case BindingDependencyKinds.Annotation when AnnotationKey.TryParse(dependency.Key, out var annotationKey):
                    var annotation = FullKey.Create(annotationKey, key.OrganizationName, key.ProjectName, dependency.Source);
                    expected.Add((annotation.GetCosmosItemId(), annotation.GetPartitionKey(), dependency.Marker));
                    break;

                default:
                    return false;
            }
        }

        if (expected.Count == 0)
        {
            return true;
        }

        var current = await ReadMarkersAsync(key.OrganizationName, expected.Select(item => (item.Id, item.PartitionKey)).Distinct().ToList());

        return current is not null
               && expected.All(item => string.Equals(
                   current.TryGetValue((item.Id, item.PartitionKey), out var marker) ? marker : BindingDependency.Absent,
                   item.Marker,
                   StringComparison.Ordinal));
    }

    // The ETags of the items that exist, by id and partition; null when the read failed.
    private async Task<Dictionary<(string Id, string PartitionKey), string>?> ReadMarkersAsync(
        string organization,
        IReadOnlyList<(string Id, string PartitionKey)> items)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;

        try
        {
            using var response = await container.ReadManyItemsStreamAsync(
                items.Select(item => (item.Id, new PartitionKey(item.PartitionKey))).ToList());

            if (!response.IsSuccessStatusCode || response.Content is null)
            {
                _logger.LogWarning(
                    "The inputs of a resolved configuration could not be revalidated ({StatusCode}): {Message}",
                    response.StatusCode,
                    response.ErrorMessage);
                return null;
            }

            using var page = await JsonDocument.ParseAsync(response.Content);
            var markers = new Dictionary<(string Id, string PartitionKey), string>();

            foreach (var element in page.RootElement.GetProperty("Documents").EnumerateArray())
            {
                if (element.TryGetProperty("id", out var id)
                    && element.TryGetProperty(nameof(Entities.Entity.PartitionKey), out var partitionKey)
                    && element.TryGetProperty("_etag", out var etag))
                {
                    markers[(id.GetString()!, partitionKey.GetString()!)] = etag.GetString()!;
                }
            }

            return markers;
        }
        catch (Exception exception) when (exception is CosmosException or OperationCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "The inputs of a resolved configuration could not be revalidated.");
            return null;
        }
    }
}
