using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Annotations;
using Microsoft.Azure.Cosmos;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Annotating;

// Loads the items of annotations of one organization from the container organizations.
internal sealed class AnnotationItemStore
{
    private readonly OrganizationContainer _repository;
    private readonly AnnotationItemSerializer _serializer;

    public AnnotationItemStore(OrganizationContainer repository, AnnotationItemSerializer serializer)
    {
        _repository = repository;
        _serializer = serializer;
    }

    // The organization of the store, the keys of its items and its batches (BatchPlan).
    public OrganizationContainer Repository => _repository;

    // Whether the annotation item exists and is active, without deserializing it. The key carries its organization.
    public static async Task<AnnotationItemState> ReadStateAsync(Container container, FullKey key)
    {
        using var response = await container.ReadItemStreamAsync(
            AnnotationItemIds.Annotation(key.ViewName, key.Annotation.ToString()),
            key.GetCosmosPartitionKey());

        if (response.StatusCode == HttpStatusCode.NotFound || response.Content is null)
        {
            return AnnotationItemState.Missing;
        }

        EnsureSuccess(response);
        using var document = await JsonDocument.ParseAsync(response.Content);

        return document.RootElement.TryGetProperty(nameof(AnnotationEntity.Deletion), out var deletion)
               && deletion.ValueKind == JsonValueKind.Object
            ? AnnotationItemState.Deleted
            : AnnotationItemState.Active;
    }

    // The states of the annotation items of the keys, by key, in one request and without deserializing them. Each key
    // carries its organization.
    public static async Task<IReadOnlyDictionary<string, AnnotationItemState>> ReadStatesAsync(
        Container container,
        IEnumerable<FullKey> keys,
        CancellationToken cancellationToken = default)
    {
        var states = new Dictionary<string, AnnotationItemState>(StringComparer.Ordinal);
        var keysById = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<(string Id, PartitionKey PartitionKey)>();

        foreach (var key in keys)
        {
            var keyText = key.Annotation.ToString();
            var id = AnnotationItemIds.Annotation(key.ViewName, keyText);

            if (keysById.TryAdd(id, keyText))
            {
                states[keyText] = AnnotationItemState.Missing;
                items.Add((id, key.GetCosmosPartitionKey()));
            }
        }

        if (items.Count == 0)
        {
            return states;
        }

        using var response = await container.ReadManyItemsStreamAsync(items, cancellationToken: cancellationToken);
        EnsureSuccess(response);
        using var page = await JsonDocument.ParseAsync(response.Content, cancellationToken: cancellationToken);

        foreach (var element in page.RootElement.GetProperty("Documents").EnumerateArray())
        {
            if (element.TryGetProperty("id", out var id)
                && id.GetString() is { } idText
                && keysById.TryGetValue(idText, out var keyText))
            {
                states[keyText] = element.TryGetProperty(nameof(AnnotationEntity.Deletion), out var deletion)
                                  && deletion.ValueKind == JsonValueKind.Object
                    ? AnnotationItemState.Deleted
                    : AnnotationItemState.Active;
            }
        }

        return states;
    }

    public async Task<AnnotationNode> LoadAsync(FullKey key, bool withConfiguration = true, bool withAudit = true)
    {
        var node = new AnnotationNode(key);
        var partitionKey = _repository.Key(node.PartitionKey);

        var annotationTask = ReadDocumentAsync(node.AnnotationId, partitionKey);
        var configurationTask = withConfiguration ? ReadDocumentAsync(node.ConfigurationId, partitionKey) : Task.FromResult<(JsonDocument?, string?)>((null, null));
        var auditTask = withAudit ? ReadDocumentAsync(node.AuditId, partitionKey) : Task.FromResult<(JsonDocument?, string?)>((null, null));

        var (annotation, annotationETag) = await annotationTask;
        var (configuration, _) = await configurationTask;
        var (audit, auditETag) = await auditTask;

        using (annotation)
        using (configuration)
        using (audit)
        {
            if (annotation is not null)
            {
                node.Entity = _serializer.ReadAnnotation(annotation.RootElement);
                node.ETag = annotationETag;
            }

            if (configuration is not null)
            {
                node.Configuration = _serializer.ReadConfiguration(configuration.RootElement);
            }

            if (audit is not null)
            {
                node.Audit = _serializer.ReadAudit(audit.RootElement);
                node.AuditETag = auditETag;
            }
        }

        return node;
    }

    // Completes a node loaded without its configuration or audit, once it is clear that the write needs them.
    public async Task LoadDetailsAsync(AnnotationNode node, bool withConfiguration, bool withAudit)
    {
        var partitionKey = _repository.Key(node.PartitionKey);
        var configurationTask = withConfiguration ? ReadDocumentAsync(node.ConfigurationId, partitionKey) : Task.FromResult<(JsonDocument?, string?)>((null, null));
        var auditTask = withAudit ? ReadDocumentAsync(node.AuditId, partitionKey) : Task.FromResult<(JsonDocument?, string?)>((null, null));

        var (configuration, _) = await configurationTask;
        var (audit, auditETag) = await auditTask;

        using (configuration)
        using (audit)
        {
            if (configuration is not null)
            {
                node.Configuration = _serializer.ReadConfiguration(configuration.RootElement);
            }

            if (audit is not null)
            {
                node.Audit = _serializer.ReadAudit(audit.RootElement);
                node.AuditETag = auditETag;
            }
        }
    }

    // Every descendant of the root in the inheritance graph, active or deleted, with its configuration and audit.
    public async Task<Dictionary<string, AnnotationNode>> LoadDescendantsAsync(FullKey root)
    {
        var nodes = new Dictionary<string, AnnotationNode>(StringComparer.Ordinal);
        var view = root.ViewName;
        var configurationPrefix = $"{view}.{EntityIdPrefixTypes.Configuration}.";
        var auditPrefix = $"{view}.{EntityIdPrefixTypes.AnnotationAudit}.";

        foreach (var scope in DescendantScope.Get(root))
        {
            await foreach (var (document, etag) in QueryScopeAsync(root, scope))
            {
                using (document)
                {
                    var element = document.RootElement;
                    var id = element.GetProperty("id").GetString() ?? string.Empty;

                    if (id.StartsWith(configurationPrefix, StringComparison.Ordinal))
                    {
                        var node = GetNode(nodes, root, id[configurationPrefix.Length..]);
                        if (node is not null)
                        {
                            node.Configuration = _serializer.ReadConfiguration(element);
                        }
                    }
                    else if (id.StartsWith(auditPrefix, StringComparison.Ordinal))
                    {
                        var node = GetNode(nodes, root, id[auditPrefix.Length..]);
                        if (node is not null)
                        {
                            node.Audit = _serializer.ReadAudit(element);
                            node.AuditETag = etag;
                        }
                    }
                    else
                    {
                        var entity = _serializer.ReadAnnotation(element);
                        var node = entity is null ? null : GetNode(nodes, root, entity.AnnotationKey);
                        if (node is not null)
                        {
                            node.Entity = entity;
                            node.ETag = etag;
                        }
                    }
                }
            }
        }

        // a configuration or an audit without its annotation is not a descendant
        foreach (var orphan in nodes.Where(pair => pair.Value.Entity is null).Select(pair => pair.Key).ToList())
        {
            nodes.Remove(orphan);
        }

        return nodes;
    }

    // Whether an item exists, by a point read of its stream.
    public async Task<bool> ExistsAsync(string id, string partitionKey)
    {
        using var response = await _repository.Container.ReadItemStreamAsync(id, _repository.Key(partitionKey));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        EnsureSuccess(response);
        return true;
    }

    // The annotation items of the keys in one request; missing keys are left out.
    public async Task<IReadOnlyList<AnnotationNode>> ReadManyAsync(IEnumerable<FullKey> keys)
    {
        var requested = keys
            .Select(key => new AnnotationNode(key))
            .GroupBy(node => node.AnnotationId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (requested.Count == 0)
        {
            return [];
        }

        var byId = requested.ToDictionary(node => node.AnnotationId, StringComparer.Ordinal);
        using var response = await _repository.Container.ReadManyItemsStreamAsync(
            requested.Select(node => (node.AnnotationId, _repository.Key(node.PartitionKey))).ToList());

        EnsureSuccess(response);

        var found = new List<AnnotationNode>();
        using var page = await JsonDocument.ParseAsync(response.Content);

        foreach (var element in page.RootElement.GetProperty("Documents").EnumerateArray())
        {
            var id = element.GetProperty("id").GetString() ?? string.Empty;

            if (!byId.TryGetValue(id, out var node))
            {
                continue;
            }

            node.Entity = _serializer.ReadAnnotation(element);
            node.ETag = element.TryGetProperty("_etag", out var etag) ? etag.GetString() : null;

            if (node.Entity is not null)
            {
                found.Add(node);
            }
        }

        return found;
    }

    // One page of annotation items of an organization-wide query, with the continuation of the next page. The text of
    // the query has "c.Organization = @organization" (the isolation); the store adds the parameter and routes the
    // query by the organization's key prefix.
    public async Task<(IReadOnlyList<AnnotationEntity> Entities, string? ContinuationToken)> QueryPageAsync(QueryDefinition query, string? continuationToken)
    {
        using var iterator = _repository.Container.GetItemQueryStreamIterator(
            _repository.WithOrganization(query),
            string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken,
            _repository.QueryOptions(maxItemCount: CosmosConstants.MaxItemCountPerPage));

        if (!iterator.HasMoreResults)
        {
            return ([], null);
        }

        using var response = await iterator.ReadNextAsync();
        EnsureSuccess(response);

        var entities = new List<AnnotationEntity>();
        using var page = await JsonDocument.ParseAsync(response.Content);

        foreach (var element in page.RootElement.GetProperty("Documents").EnumerateArray())
        {
            if (_serializer.ReadAnnotation(element) is { } entity)
            {
                entities.Add(entity);
            }
        }

        return (entities, response.ContinuationToken);
    }

    private static AnnotationNode? GetNode(Dictionary<string, AnnotationNode> nodes, FullKey root, string keyText)
    {
        if (nodes.TryGetValue(keyText, out var node))
        {
            return node;
        }

        if (!AnnotationKey.TryParse(keyText, out var key)
            || !key.IsDescendantOf(root.Annotation))
        {
            return null;
        }

        node = new AnnotationNode(FullKey.Create(key, root));
        nodes[keyText] = node;
        return node;
    }

    private static void EnsureSuccess(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new CosmosException(
                $"Cosmos DB request failed: {response.ErrorMessage}",
                response.StatusCode,
                0,
                response.Headers.ActivityId,
                response.Headers.RequestCharge);
        }
    }

    private async Task<(JsonDocument? Document, string? ETag)> ReadDocumentAsync(string id, PartitionKey partitionKey)
    {
        using var response = await _repository.Container.ReadItemStreamAsync(id, partitionKey);

        if (response.StatusCode == HttpStatusCode.NotFound || response.Content is null)
        {
            return (null, null);
        }

        EnsureSuccess(response);
        return (await JsonDocument.ParseAsync(response.Content), response.Headers.ETag);
    }

    private async IAsyncEnumerable<(JsonDocument Document, string? ETag)> QueryScopeAsync(FullKey root, DescendantScope scope)
    {
        var view = root.ViewName;
        var prefixes = scope.KeyPrefixes
            .SelectMany(prefix => new[]
            {
                $"{view}.{prefix}",
                $"{view}.{EntityIdPrefixTypes.Configuration}.{prefix}",
                $"{view}.{EntityIdPrefixTypes.AnnotationAudit}.{prefix}",
            })
            .ToList();

        var text = new StringBuilder("SELECT * FROM c WHERE (");
        text.Append(string.Join(" OR ", prefixes.Select((_, index) => $"STARTSWITH(c.id, @p{index})")));
        text.Append(')');

        // across the partitions of the project: an organization-wide query, isolated by its organization
        if (scope.PartitionKey is null)
        {
            text.Append($" AND c.Organization = {OrganizationContainer.OrganizationParameter} AND STARTSWITH(c.PartitionKey, @partitionPrefix)");
        }

        var query = new QueryDefinition(text.ToString());

        for (var index = 0; index < prefixes.Count; index++)
        {
            query = query.WithParameter($"@p{index}", prefixes[index]);
        }

        if (scope.PartitionKey is null)
        {
            query = _repository.WithOrganization(query).WithParameter("@partitionPrefix", scope.PartitionPrefix);
        }

        // one partition, or the organization's key prefix when the scope spans partitions
        using var iterator = _repository.Container.GetItemQueryStreamIterator(
            query,
            requestOptions: _repository.QueryOptions(scope.PartitionKey, CosmosConstants.MaxItemCountPerPage));

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            EnsureSuccess(response);

            using var page = await JsonDocument.ParseAsync(response.Content);

            foreach (var element in page.RootElement.GetProperty("Documents").EnumerateArray())
            {
                var etag = element.TryGetProperty("_etag", out var etagProperty) ? etagProperty.GetString() : null;
                yield return (JsonDocument.Parse(element.GetRawText()), etag);
            }
        }
    }
}
