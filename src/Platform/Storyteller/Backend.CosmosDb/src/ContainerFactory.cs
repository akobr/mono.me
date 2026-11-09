using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Utils.Async;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class ContainerFactory : IContainerFactory
{
    // The container organizations: TTL on without a default expiry (only items with a ttl expire: versions, and the
    // items of soft-deleted annotations), and the audit entries left out of the index.
    public const string AuditEntriesPath = "/Entries/*";

    // The audit stamps and actors are read with their items but never filtered or sorted on, so they stay out of the
    // index, where they would cost index terms on every write (on the emulator, an annotation write drops from about
    // 15 to 10 RU). Only the deletion times stay indexed: the deleted listings filter and sort on them.
    public static readonly IReadOnlyList<string> AuditStampPaths =
    [
        "/Created/*",
        "/Modified/*",
        "/Deleted/By/*",
        "/Deletion/DeletedBy/*",
        "/Actor/*",
    ];

    // The hierarchical partition key of the container organizations: the organization, then the partition each item has
    // always had ({project}.rst.{name}, {project}.kv.{view}, ...).
    public static readonly IReadOnlyList<string> OrganizationsPartitionKeyPaths =
    [
        OrganizationKeys.OrganizationPath,
        OrganizationKeys.PartitionKeyPath,
    ];

    private readonly ICosmosClientProvider _cosmosClientProvider;
    private readonly CosmosDbOptions _options;
    private readonly AsyncLazy<Database> _database;

    public ContainerFactory(ICosmosClientProvider cosmosClientProvider, IOptions<CosmosDbOptions> options)
    {
        _cosmosClientProvider = cosmosClientProvider;
        _options = options.Value;
        _database = new AsyncLazy<Database>(BuildDatabase);
    }

    // The container organizations gets its hierarchical key and its settings; any other container (core) the single
    // path /PartitionKey.
    public async Task<Container> CreateContainerIfNotExistsAsync(string containerName)
    {
        if (string.Equals(containerName, CosmosConstants.OrganizationsContainerName, StringComparison.Ordinal))
        {
            return await EnsureOrganizationsContainerAsync();
        }

        var database = await _database;
        var properties = new ContainerProperties(containerName, OrganizationKeys.PartitionKeyPath);
        var containerResult = await database.CreateContainerIfNotExistsAsync(properties);
        return containerResult.Container;
    }

    public async Task<Container> EnsureOrganizationsContainerAsync()
    {
        var database = await _database;
        var properties = new ContainerProperties(CosmosConstants.OrganizationsContainerName, OrganizationsPartitionKeyPaths.ToList());
        ApplySettings(properties);

        var containerResult = await database.CreateContainerIfNotExistsAsync(properties);

        // an existing container keeps its definition: one created before the settings existed gets them now
        await EnsureContainerSettingsAsync(containerResult.Container);
        return containerResult.Container;
    }

    // Containers created before the settings existed get them on the next start of DbCreator.
    public async Task EnsureContainerSettingsAsync(Container container)
    {
        var response = await container.ReadContainerAsync();
        var properties = response.Resource;

        if (ApplySettings(properties))
        {
            await container.ReplaceContainerAsync(properties);
        }
    }

    // Reads only: the database is not created when it is missing.
    public async Task<IReadOnlyList<string>> FindLayoutProblemsAsync()
    {
        var problems = new List<string>();
        var database = _cosmosClientProvider.Client.GetDatabase(CosmosConstants.DatabaseName);
        var containers = new List<ContainerProperties>();

        try
        {
            using var iterator = database.GetContainerQueryIterator<ContainerProperties>();

            while (iterator.HasMoreResults)
            {
                containers.AddRange(await iterator.ReadNextAsync());
            }
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return problems;
        }

        foreach (var properties in containers.OrderBy(properties => properties.Id, StringComparer.Ordinal))
        {
            if (properties.Id.StartsWith(CosmosConstants.LegacyOrganizationContainerNamePrefix, StringComparison.Ordinal))
            {
                problems.Add($"The container '{properties.Id}' holds one organization in the layout before the shared container '{CosmosConstants.OrganizationsContainerName}'.");
            }
            else if (string.Equals(properties.Id, CosmosConstants.OrganizationsContainerName, StringComparison.Ordinal)
                     && !HasOrganizationsPartitionKey(properties))
            {
                var paths = properties.PartitionKeyPaths is { Count: > 0 } keyPaths ? keyPaths : [properties.PartitionKeyPath];
                problems.Add($"The container '{properties.Id}' is partitioned by {string.Join(", ", paths)}, not by {string.Join(", ", OrganizationsPartitionKeyPaths)}.");
            }
        }

        return problems;
    }

    // Names of the containers of the platform database.
    public async Task<IReadOnlyList<string>> GetContainerNamesAsync()
    {
        var database = await _database;
        var names = new List<string>();
        using var iterator = database.GetContainerQueryIterator<ContainerProperties>();

        while (iterator.HasMoreResults)
        {
            names.AddRange((await iterator.ReadNextAsync()).Select(properties => properties.Id));
        }

        return names;
    }

    private static bool HasOrganizationsPartitionKey(ContainerProperties properties)
    {
        return properties.PartitionKeyPaths is { } paths
               && paths.SequenceEqual(OrganizationsPartitionKeyPaths, StringComparer.Ordinal);
    }

    // Returns whether anything changed.
    private static bool ApplySettings(ContainerProperties properties)
    {
        var changed = false;

        // null: TTL off, so the ttl of versions and deleted items is ignored; positive: every item without its own ttl
        // expires, which active records never may. -1 keeps TTL on with no default expiry.
        if (properties.DefaultTimeToLive is null or > 0)
        {
            properties.DefaultTimeToLive = -1;
            changed = true;
        }

        var policy = properties.IndexingPolicy;

        if (policy.IncludedPaths.Count == 0)
        {
            policy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
            changed = true;
        }

        foreach (var excludedPath in AuditStampPaths.Prepend(AuditEntriesPath))
        {
            if (!policy.ExcludedPaths.Any(path => string.Equals(path.Path, excludedPath, StringComparison.Ordinal)))
            {
                policy.ExcludedPaths.Add(new ExcludedPath { Path = excludedPath });
                changed = true;
            }
        }

        return changed;
    }

    private async Task<Database> BuildDatabase()
    {
        ThroughputProperties? throughput = _options.ManualThroughput.HasValue
            ? ThroughputProperties.CreateManualThroughput(_options.ManualThroughput.Value)
            : _options.AutoscaleMaxThroughput.HasValue
                ? ThroughputProperties.CreateAutoscaleThroughput(_options.AutoscaleMaxThroughput.Value)
                : null;

        var databaseResponse = await _cosmosClientProvider.Client.CreateDatabaseIfNotExistsAsync(
            CosmosConstants.DatabaseName,
            throughput);
        return databaseResponse.Database;
    }
}
