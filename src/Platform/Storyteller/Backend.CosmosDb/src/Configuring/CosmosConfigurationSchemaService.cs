using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public class CosmosConfigurationSchemaService : IConfigurationSchemaService
{
    private const string SchemaPartitionSuffix = "schema";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerOptions;

    public CosmosConfigurationSchemaService(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerSettings> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string annotationType)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        annotationType = NormalizeAnnotationType(annotationType);
        var id = GetSchemaEntityId(annotationType);

        var entity = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));

        return entity is null ? null : ToModel(entity);
    }

    public async Task<ConfigurationSchema> SetSchemaAsync(
        string organization,
        string project,
        string annotationType,
        JObject schemaContent,
        string author)
    {
        JsonSchema schema;
        try
        {
            var schemaJson = schemaContent.ToString(Formatting.None);
            schema = await JsonSchema.FromJsonAsync(schemaJson);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid JSON Schema: {ex.Message}", ex);
        }

        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var validationErrors = await ValidateExistingConfigurationsAsync(repository, project, annotationType, schema);

        if (validationErrors.Count > 0)
        {
            throw new SchemaValidationException(validationErrors);
        }

        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetSchemaEntityId(annotationType);

        var entity = await UpsertWithOptimisticConcurrencyAsync(
            repository.Container,
            id,
            partitionKey,
            existing => new ConfigurationSchemaEntity
            {
                PartitionKey = partitionKeyValue,
                Id = id,
                AnnotationKey = annotationType,
                Name = annotationType,
                ProjectName = project,
                ViewName = string.Empty,
                Content = schemaContent,
                Author = author,
                Version = existing is null ? 1UL : existing.Version + 1,
            });

        return ToModel(entity);
    }

    public async Task<bool> DeleteSchemaAsync(string organization, string project, string annotationType)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        annotationType = NormalizeAnnotationType(annotationType);
        var id = GetSchemaEntityId(annotationType);

        try
        {
            await repository.Container.DeleteItemAsync<ConfigurationSchemaEntity>(id, partitionKey);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string annotationKey)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetAnnotationSchemaEntityId(annotationKey);

        var entity = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));

        return entity is null ? null : ToAnnotationModel(entity, annotationKey);
    }

    public async Task<ConfigurationSchema> SetAnnotationSchemaAsync(
        string organization,
        string project,
        string annotationKey,
        JObject schemaContent,
        string author)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);

        JsonSchema schema;
        try
        {
            var schemaJson = schemaContent.ToString(Formatting.None);
            schema = await JsonSchema.FromJsonAsync(schemaJson);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid JSON Schema: {ex.Message}", ex);
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var validationErrors = await ValidateExistingAnnotationConfigurationsAsync(repository, project, annotationKey, schema);

        if (validationErrors.Count > 0)
        {
            throw new SchemaValidationException(validationErrors);
        }

        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetAnnotationSchemaEntityId(annotationKey);

        var entity = await UpsertWithOptimisticConcurrencyAsync(
            repository.Container,
            id,
            partitionKey,
            existing => new ConfigurationSchemaEntity
            {
                PartitionKey = partitionKeyValue,
                Id = id,
                AnnotationKey = annotationKey,
                Name = annotationKey,
                ProjectName = project,
                ViewName = string.Empty,
                Content = schemaContent,
                Author = author,
                Version = existing is null ? 1UL : existing.Version + 1,
            });

        return ToAnnotationModel(entity, annotationKey);
    }

    public async Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string annotationKey)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetAnnotationSchemaEntityId(annotationKey);

        try
        {
            await repository.Container.DeleteItemAsync<ConfigurationSchemaEntity>(id, partitionKey);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string annotationKey,
        string descendantTypeCode)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        descendantTypeCode = NormalizeAnnotationType(descendantTypeCode);
        var id = GetDescendantTypeSchemaEntityId(descendantTypeCode, annotationKey);

        var entity = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));

        return entity is null ? null : ToDescendantTypeModel(entity, annotationKey, descendantTypeCode);
    }

    public async Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string annotationKey,
        string descendantTypeCode,
        JObject schemaContent,
        string author)
    {
        descendantTypeCode = NormalizeAnnotationType(descendantTypeCode);
        var parsedKey = AnnotationKey.Parse(annotationKey);

        if (!AnnotationTypeCodes.ValidCodes.ContainsKey(descendantTypeCode))
        {
            throw new ArgumentException($"Invalid descendant type code: {descendantTypeCode}");
        }

        JsonSchema schema;
        try
        {
            var schemaJson = schemaContent.ToString(Formatting.None);
            schema = await JsonSchema.FromJsonAsync(schemaJson);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid JSON Schema: {ex.Message}", ex);
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var validationErrors = await ValidateExistingDescendantConfigurationsAsync(repository, project, annotationKey, descendantTypeCode, schema);

        if (validationErrors.Count > 0)
        {
            throw new SchemaValidationException(validationErrors);
        }

        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetDescendantTypeSchemaEntityId(descendantTypeCode, annotationKey);

        var entity = await UpsertWithOptimisticConcurrencyAsync(
            repository.Container,
            id,
            partitionKey,
            existing => new ConfigurationSchemaEntity
            {
                PartitionKey = partitionKeyValue,
                Id = id,
                AnnotationKey = annotationKey,
                Name = $"dt.{descendantTypeCode}.{annotationKey}",
                ProjectName = project,
                ViewName = string.Empty,
                Content = schemaContent,
                Author = author,
                Version = existing is null ? 1UL : existing.Version + 1,
            });

        return ToDescendantTypeModel(entity, annotationKey, descendantTypeCode);
    }

    public async Task<bool> DeleteDescendantTypeSchemaAsync(
        string organization,
        string project,
        string annotationKey,
        string descendantTypeCode)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetAnnotationSchemaPartitionKey(project, parsedKey);
        var partitionKey = new PartitionKey(partitionKeyValue);
        descendantTypeCode = NormalizeAnnotationType(descendantTypeCode);
        var id = GetDescendantTypeSchemaEntityId(descendantTypeCode, annotationKey);

        try
        {
            await repository.Container.DeleteItemAsync<ConfigurationSchemaEntity>(id, partitionKey);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(
        string organization,
        string project,
        string annotationKey)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        // 1. Type-level schema from {project}.schema partition
        var typeLevelTask = GetSchemaAsync(organization, project, parsedKey.TypeCode);

        // 2. Annotation-level schema from config partition
        var annotationLevelTask = GetAnnotationSchemaAsync(organization, project, annotationKey);

        // 3. Ancestor descendant-type schemas
        var ancestorSources = AnnotationHierarchy.GetAncestorSchemaSources(parsedKey);
        var ancestorTasks = new List<Task<ConfigurationSchema?>>(ancestorSources.Count);
        foreach (var (ancestor, descendantTypeCode) in ancestorSources)
        {
            ancestorTasks.Add(GetDescendantTypeSchemaAsync(organization, project, ancestor.ToString(), descendantTypeCode));
        }

        await Task.WhenAll(
            typeLevelTask,
            annotationLevelTask,
            Task.WhenAll(ancestorTasks));

        // Merge: type-level → ancestor descendant-type (in order) → annotation-level
        var appliedSchemas = new List<ConfigurationSchema>();

        var typeLevel = typeLevelTask.Result;
        if (typeLevel is not null)
        {
            appliedSchemas.Add(typeLevel);
        }

        foreach (var task in ancestorTasks)
        {
            var ancestorSchema = task.Result;
            if (ancestorSchema is not null)
            {
                appliedSchemas.Add(ancestorSchema);
            }
        }

        var annotationLevel = annotationLevelTask.Result;
        if (annotationLevel is not null)
        {
            appliedSchemas.Add(annotationLevel);
        }

        if (appliedSchemas.Count == 0)
        {
            return null;
        }

        var merged = DeepMergeSchemas(appliedSchemas);

        return new CombinedConfigurationSchema
        {
            AnnotationKey = annotationKey,
            MergedContent = merged,
            AppliedSchemas = appliedSchemas,
        };
    }

    public async Task ValidateContentAsync(string organization, string project, string annotationKey, JObject content)
    {
        var combined = await GetCombinedSchemaAsync(organization, project, annotationKey);

        if (combined is null)
        {
            return;
        }

        var schemaJson = combined.MergedContent.ToString(Formatting.None);
        var schema = await JsonSchema.FromJsonAsync(schemaJson);
        var contentJson = content.ToString(Formatting.None);
        var validationResults = schema.Validate(contentJson);

        if (validationResults.Count > 0)
        {
            var errors = new List<SchemaValidationError>
            {
                new()
                {
                    AnnotationKey = annotationKey,
                    ViewName = string.Empty,
                    Errors = validationResults.Select(e => $"{e.Path}: {e.Kind}").ToList(),
                },
            };

            throw new SchemaValidationException(errors);
        }
    }

    private async Task<IReadOnlyList<SchemaValidationError>> ValidateExistingConfigurationsAsync(
        IContainerRepository repository,
        string project,
        string annotationType,
        JsonSchema schema)
    {
        var configIdPart = $".{EntityIdPrefixTypes.Configuration}.{annotationType}.";
        var errors = new List<SchemaValidationError>();

        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: false);

        var feed = queryable
            .Where(config =>
                config.ProjectName == project
                && config.Id.Contains(configIdPart))
            .ToFeedIterator();

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            foreach (var configEntity in results)
            {
                if (!configEntity.Content.HasValues)
                {
                    continue;
                }

                var configJson = configEntity.Content.ToString(Formatting.None);
                var validationResults = schema.Validate(configJson);

                if (validationResults.Count > 0)
                {
                    errors.Add(new SchemaValidationError
                    {
                        AnnotationKey = configEntity.AnnotationKey,
                        ViewName = configEntity.ViewName,
                        Errors = validationResults.Select(e => $"{e.Path}: {e.Kind}").ToList(),
                    });
                }
            }
        }

        return errors;
    }

    private async Task<IReadOnlyList<SchemaValidationError>> ValidateExistingAnnotationConfigurationsAsync(
        IContainerRepository repository,
        string project,
        string annotationKey,
        JsonSchema schema)
    {
        var configIdEnd = $".{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var errors = new List<SchemaValidationError>();

        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: false);

        var feed = queryable
            .Where(config =>
                config.ProjectName == project
                && config.Id.EndsWith(configIdEnd))
            .ToFeedIterator();

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            foreach (var configEntity in results)
            {
                if (!configEntity.Content.HasValues)
                {
                    continue;
                }

                var configJson = configEntity.Content.ToString(Formatting.None);
                var validationResults = schema.Validate(configJson);

                if (validationResults.Count > 0)
                {
                    errors.Add(new SchemaValidationError
                    {
                        AnnotationKey = configEntity.AnnotationKey,
                        ViewName = configEntity.ViewName,
                        Errors = validationResults.Select(e => $"{e.Path}: {e.Kind}").ToList(),
                    });
                }
            }
        }

        return errors;
    }

    private async Task<IReadOnlyList<SchemaValidationError>> ValidateExistingDescendantConfigurationsAsync(
        IContainerRepository repository,
        string project,
        string annotationKey,
        string descendantTypeCode,
        JsonSchema schema)
    {
        var configIdPart = $".{EntityIdPrefixTypes.Configuration}.{descendantTypeCode}.";
        var errors = new List<SchemaValidationError>();

        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: false);

        var feed = queryable
            .Where(config =>
                config.ProjectName == project
                && config.Id.Contains(configIdPart))
            .ToFeedIterator();

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            foreach (var configEntity in results)
            {
                if (!configEntity.Content.HasValues)
                {
                    continue;
                }

                var configAnnotationKey = AnnotationKey.Parse(configEntity.AnnotationKey);
                var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(configAnnotationKey);

                if (!ancestors.Any(a => a.Ancestor == annotationKey && a.DescendantTypeCode == descendantTypeCode))
                {
                    continue;
                }

                var configJson = configEntity.Content.ToString(Formatting.None);
                var validationResults = schema.Validate(configJson);

                if (validationResults.Count > 0)
                {
                    errors.Add(new SchemaValidationError
                    {
                        AnnotationKey = configEntity.AnnotationKey,
                        ViewName = configEntity.ViewName,
                        Errors = validationResults.Select(e => $"{e.Path}: {e.Kind}").ToList(),
                    });
                }
            }
        }

        return errors;
    }

    private async Task<ConfigurationSchemaEntity> UpsertWithOptimisticConcurrencyAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        Func<ConfigurationSchemaEntity?, ConfigurationSchemaEntity> entityFactory,
        int maxRetries = 3)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var (existing, etag) = await container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));

            var entity = entityFactory(existing);

            var requestOptions = etag is not null
                ? new ItemRequestOptions { IfMatchEtag = etag }
                : null;

            try
            {
                await container.UpsertItemAsync(entity, partitionKey, requestOptions);
                return entity;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed && attempt < maxRetries)
            {
                // Another writer modified the item; retry with fresh read
            }
        }

        throw new InvalidOperationException($"Failed to update schema '{id}' after multiple retries ({maxRetries}) due to concurrent modifications.");
    }

    private static string GetSchemaPartitionKey(string project)
    {
        return $"{project}.{SchemaPartitionSuffix}";
    }

    private static string NormalizeAnnotationType(string annotationType)
    {
        return annotationType.ToLowerInvariant();
    }

    private static string GetSchemaEntityId(string annotationType)
    {
        return $"{EntityIdPrefixTypes.ConfigurationSchema}.{annotationType}";
    }

    private static string GetAnnotationSchemaPartitionKey(string project, AnnotationKey key)
    {
        return key.Type is AnnotationType.Subject or AnnotationType.Context
            ? $"{project}.{AnnotationTypeCodes.Subject}.{key.SubjectName}"
            : $"{project}.{AnnotationTypeCodes.Responsibility}.{key.ResponsibilityName}";
    }

    private static string GetAnnotationSchemaEntityId(string annotationKey)
    {
        return $"{EntityIdPrefixTypes.ConfigurationSchema}.{annotationKey}";
    }

    private static string GetDescendantTypeSchemaEntityId(string descendantTypeCode, string annotationKey)
    {
        return $"{EntityIdPrefixTypes.ConfigurationSchema}.dt.{descendantTypeCode}.{annotationKey}";
    }

    private static ConfigurationSchema ToModel(ConfigurationSchemaEntity entity)
    {
        return new ConfigurationSchema
        {
            AnnotationType = entity.AnnotationKey,
            Version = entity.Version,
            Content = entity.Content,
            Author = entity.Author,
        };
    }

    private static ConfigurationSchema ToAnnotationModel(ConfigurationSchemaEntity entity, string annotationKey)
    {
        return new ConfigurationSchema
        {
            AnnotationKey = annotationKey,
            Version = entity.Version,
            Content = entity.Content,
            Author = entity.Author,
        };
    }

    private static ConfigurationSchema ToDescendantTypeModel(ConfigurationSchemaEntity entity, string annotationKey, string descendantTypeCode)
    {
        return new ConfigurationSchema
        {
            AnnotationType = descendantTypeCode,
            AnnotationKey = annotationKey,
            Version = entity.Version,
            Content = entity.Content,
            Author = entity.Author,
        };
    }

    internal static JObject DeepMergeSchemas(IReadOnlyList<ConfigurationSchema> schemas)
    {
        if (schemas.Count == 0)
        {
            return new JObject();
        }

        var merged = (JObject)schemas[0].Content.DeepClone();

        for (var i = 1; i < schemas.Count; i++)
        {
            var overlay = schemas[i].Content;
            DeepMergeInto(merged, overlay);
        }

        return merged;
    }

    private static void DeepMergeInto(JObject target, JObject source)
    {
        foreach (var property in source.Properties())
        {
            var existing = target.Property(property.Name);

            if (existing is null)
            {
                target.Add(property.Name, property.Value.DeepClone());
                continue;
            }

            if (property.Name == "required"
                && existing.Value is JArray existingArray
                && property.Value is JArray sourceArray)
            {
                // Union required arrays
                var existingValues = existingArray.Select(t => t.ToString()).ToHashSet();
                foreach (var item in sourceArray)
                {
                    if (existingValues.Add(item.ToString()))
                    {
                        existingArray.Add(item.DeepClone());
                    }
                }

                continue;
            }

            if (existing.Value is JObject existingObj && property.Value is JObject sourceObj)
            {
                DeepMergeInto(existingObj, sourceObj);
                continue;
            }

            // More specific overwrites less specific
            existing.Value = property.Value.DeepClone();
        }
    }
}
