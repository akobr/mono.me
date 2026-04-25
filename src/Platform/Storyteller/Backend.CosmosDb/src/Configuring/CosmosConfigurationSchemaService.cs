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
        // 1. Parse and validate the JSON schema itself
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

        // 2. Load all existing configurations of this annotation type and validate them
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var validationErrors = await ValidateExistingConfigurationsAsync(repository, project, annotationType, schema);

        if (validationErrors.Count > 0)
        {
            throw new SchemaValidationException(validationErrors);
        }

        // 3. Upsert the schema entity
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetSchemaEntityId(annotationType);

        var existingEntity = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));

        var newVersion = existingEntity is null ? 1UL : existingEntity.Version + 1;

        var entity = new ConfigurationSchemaEntity
        {
            PartitionKey = partitionKeyValue,
            Id = id,
            AnnotationKey = annotationType,
            Name = annotationType,
            ProjectName = project,
            ViewName = string.Empty,
            Content = schemaContent,
            Author = author,
            Version = newVersion,
        };

        await repository.Container.UpsertItemAsync(entity, partitionKey);
        return ToModel(entity);
    }

    public async Task<bool> DeleteSchemaAsync(string organization, string project, string annotationType)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
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

    private async Task<IReadOnlyList<SchemaValidationError>> ValidateExistingConfigurationsAsync(
        IContainerRepository repository,
        string project,
        string annotationType,
        JsonSchema schema)
    {
        var configPrefix = $"{EntityIdPrefixTypes.Configuration}.{annotationType}.";
        var errors = new List<SchemaValidationError>();

        // Query all configuration entities across all partitions that match the annotation type
        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: false);

        var feed = queryable
            .Where(config =>
                config.ProjectName == project
                && config.Id.Contains($".{configPrefix}"))
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

    private static string GetSchemaPartitionKey(string project)
    {
        return $"{project}.{SchemaPartitionSuffix}";
    }

    private static string GetSchemaEntityId(string annotationType)
    {
        return $"{EntityIdPrefixTypes.ConfigurationSchema}.{annotationType}";
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
}
