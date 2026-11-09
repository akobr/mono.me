using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Annotations;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Annotating;

public class CosmosViewService : IViewService
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly TimeProvider _timeProvider;

    public CosmosViewService(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        TimeProvider? timeProvider = null)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<View>> GetViewsAsync(string organization, string project, bool discover)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var views = (await GetRegisteredAsync(repository, project))
            .ToDictionary(view => view.Name, StringComparer.Ordinal);

        views.TryAdd(Constants.DefaultViewName, new View { Name = Constants.DefaultViewName, IsDefault = true });

        if (discover)
        {
            foreach (var name in await DiscoverAsync(repository, project))
            {
                views.TryAdd(name, new View { Name = name });
            }
        }

        return views.Values
            .OrderByDescending(view => view.IsDefault)
            .ThenBy(view => view.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<View> CreateViewAsync(string organization, string project, ViewCreate model, AuditActor actor)
    {
        var name = model.Name?.Trim();
        NameRules.EnsureViewName(name);

        if (name == Constants.DefaultViewName)
        {
            throw new ConflictException($"The view '{name}' always exists.", ErrorCodes.ViewExists);
        }

        var entity = new ViewEntity
        {
            PartitionKey = ViewEntity.GetPartitionKey(project),
            Organization = organization,
            Name = name!,
            Description = NormalizeDescription(model.Description),
            Created = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
        };

        try
        {
            var repository = _repositoryProvider.GetOrganizationContainer(organization);
            await repository.Container.CreateItemAsync(entity, repository.Key(entity.PartitionKey));
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ConflictException($"The view '{name}' is already registered in '{organization}.{project}'.", ErrorCodes.ViewExists);
        }

        return entity.ToView();
    }

    public async Task<View> UpdateViewAsync(string organization, string project, string view, ViewUpdate model, AuditActor actor)
    {
        // The name is kept as given: legacy names that break the rules are rejected, never registered under another id.
        var name = view.Trim();

        // "default" predates the name rules and is always valid; any other new registration follows them.
        if (name != Constants.DefaultViewName)
        {
            NameRules.EnsureViewName(name);
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = ViewEntity.GetPartitionKey(project);
        var existing = await repository.Container.TryReadItemAsync(
            $"{ViewEntity.IdPrefix}{name}",
            repository.Key(partitionKey),
            stream => stream.DeserializeSystemTextJson<ViewEntity>(_serializerOptions));

        var entity = (existing ?? new ViewEntity
        {
            PartitionKey = partitionKey,
            Organization = organization,
            Name = name,
            Created = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
        }) with
        {
            Description = NormalizeDescription(model.Description),
        };

        await repository.Container.UpsertItemAsync(entity, repository.Key(partitionKey));
        return entity.ToView();
    }

    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private async Task<List<View>> GetRegisteredAsync(OrganizationContainer repository, string project)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", ViewEntity.IdPrefix);
        using var iterator = repository.Container.GetItemQueryStreamIterator(
            query,
            requestOptions: repository.QueryOptions(ViewEntity.GetPartitionKey(project)));
        var views = new List<View>();

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            var page = await JsonSerializer.DeserializeAsync<QueryPage<ViewEntity>>(response.Content, _serializerOptions);
            views.AddRange((page?.Documents ?? []).Select(entity => entity.ToView()));
        }

        return views;
    }

    // Every view that holds any document of the project: annotations, configurations, templates, schemas and their history.
    // An organization-wide query over every partition of the organization (its key prefix, isolated by the predicate
    // on Organization); meant for administrators, not for every page load.
    private static async Task<List<string>> DiscoverAsync(OrganizationContainer repository, string project)
    {
        var query = repository.WithOrganization(new QueryDefinition(
                $"SELECT DISTINCT VALUE c.ViewName FROM c WHERE c.Organization = {OrganizationContainer.OrganizationParameter} "
                + "AND STARTSWITH(c.PartitionKey, @prefix) AND IS_DEFINED(c.ViewName)")
            .WithParameter("@prefix", $"{project}."));
        using var iterator = repository.Container.GetItemQueryIterator<string>(query, requestOptions: repository.QueryOptions());
        var names = new List<string>();

        while (iterator.HasMoreResults)
        {
            names.AddRange((await iterator.ReadNextAsync()).Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        return names;
    }

    private sealed record QueryPage<T>(List<T>? Documents);
}
