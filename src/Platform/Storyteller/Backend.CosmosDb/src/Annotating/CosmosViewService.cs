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
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var views = (await GetRegisteredAsync(container, project))
            .ToDictionary(view => view.Name, StringComparer.Ordinal);

        views.TryAdd(Constants.DefaultViewName, new View { Name = Constants.DefaultViewName, IsDefault = true });

        if (discover)
        {
            foreach (var name in await DiscoverAsync(container, project))
            {
                views.TryAdd(name, new View { Name = name });
            }
        }

        return views.Values
            .OrderByDescending(view => view.IsDefault)
            .ThenBy(view => view.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<View> CreateViewAsync(string organization, string project, ViewCreate model, string author)
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
            Name = name!,
            Description = NormalizeDescription(model.Description),
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = author,
        };

        try
        {
            var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
            await container.CreateItemAsync(entity, new PartitionKey(entity.PartitionKey));
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ConflictException($"The view '{name}' is already registered in '{organization}.{project}'.", ErrorCodes.ViewExists);
        }

        return entity.ToView();
    }

    public async Task<View> UpdateViewAsync(string organization, string project, string view, ViewUpdate model, string author)
    {
        var name = view.Trim().ToLowerInvariant();

        // "default" predates the name rules and is always valid; any other new registration follows them.
        if (name != Constants.DefaultViewName)
        {
            NameRules.EnsureViewName(name);
        }

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var partitionKey = ViewEntity.GetPartitionKey(project);
        var existing = await container.TryReadItemAsync(
            $"{ViewEntity.IdPrefix}{name}",
            new PartitionKey(partitionKey),
            stream => stream.DeserializeSystemTextJson<ViewEntity>(_serializerOptions));

        var entity = (existing ?? new ViewEntity
        {
            PartitionKey = partitionKey,
            Name = name,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = author,
        }) with
        {
            Description = NormalizeDescription(model.Description),
        };

        await container.UpsertItemAsync(entity, new PartitionKey(partitionKey));
        return entity.ToView();
    }

    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private async Task<List<View>> GetRegisteredAsync(Container container, string project)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", ViewEntity.IdPrefix);
        using var iterator = container.GetItemQueryStreamIterator(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ViewEntity.GetPartitionKey(project)) });
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
    // A cross-partition query over the organization container; meant for administrators, not for every page load.
    private static async Task<List<string>> DiscoverAsync(Container container, string project)
    {
        var query = new QueryDefinition("SELECT DISTINCT VALUE c.ViewName FROM c WHERE STARTSWITH(c.PartitionKey, @prefix) AND IS_DEFINED(c.ViewName)")
            .WithParameter("@prefix", $"{project}.");
        using var iterator = container.GetItemQueryIterator<string>(query);
        var names = new List<string>();

        while (iterator.HasMoreResults)
        {
            names.AddRange((await iterator.ReadNextAsync()).Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        return names;
    }

    private sealed record QueryPage<T>(List<T>? Documents);
}
