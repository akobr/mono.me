using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Keeps the hash on the machine document. Every write patches only the fields the store owns: a copy of the whole
// document would bring back a machine deleted meanwhile, or undo a concurrent write such as a certificate renewal.
public class CosmosMergedApiKeyHashStore : IApiKeyHashStore
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosMergedApiKeyHashStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task StoreAsync(
        string organization,
        string project,
        string machineAccessId,
        string hashedSecret,
        IReadOnlyList<MachineScope> scopes)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);

        // Unlike ReplaceAsync, this creates the machine when it is missing: it runs on create, before the machine exists.
        // A stored machine gets only its two fields patched; a missing one answers 404 and gets the partial entity below.
        using var response = await repository.Container.PatchItemStreamAsync(
            machineAccessId,
            partitionKey,
            [
                PatchOperation.Set($"/{nameof(MachineAccessEntity.HashedSecret)}", hashedSecret),
                PatchOperation.Set($"/{nameof(MachineAccessEntity.Scopes)}", scopes.ToList()),
            ]);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
            return;
        }

        // Entity doesn't exist yet (StoreAsync is called before CosmosAccessService creates it).
        // Create a partial entity with the hash — CosmosAccessService.CreateMachineAccessAsync
        // will upsert the full entity on top of this.
        var partial = new MachineAccessEntity
        {
            PartitionKey = partitionKeyValue,
            Organization = repository.Organization,
            Id = machineAccessId,
            ObjectId = machineAccessId,
            AccessKey = "***",
            Scopes = scopes.ToList(),
            HashedSecret = hashedSecret,
        };

        await repository.Container.UpsertItemAsync(partial, partitionKey);
    }

    public async Task<bool> ReplaceAsync(
        string organization,
        string project,
        string machineAccessId,
        string currentHashedSecret,
        string newHashedSecret)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);

        // A concurrent write to another field (a certificate renewal) only moves the ETag, so a few retries suffice.
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            var machineAccess = await repository.Container.TryReadItemAsync(
                machineAccessId,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

            // A delete removes the hash before the document, so a removed hash is a machine on its way out.
            if (machineAccess?.HashedSecret is null)
            {
                throw new NotFoundException($"The machine access {machineAccessId} has not been found.");
            }

            if (!string.Equals(machineAccess.HashedSecret, currentHashedSecret, StringComparison.Ordinal))
            {
                return false;
            }

            // The new hash is the reset: when the access service opened the record of this reset, it is patched in
            // one batch with it, so the key never changes without its access audit record.
            var pending = PendingAccessAudit.Find(repository.Organization, partitionKeyValue, machineAccessId);
            var status = pending is null
                ? await PatchHashAsync(repository, machineAccessId, partitionKey, newHashedSecret, machineAccess.ETag)
                : await PatchHashWithRecordAsync(repository, machineAccessId, partitionKey, newHashedSecret, machineAccess.ETag, pending);

            if (status == HttpStatusCode.NotFound)
            {
                throw new NotFoundException($"The machine access {machineAccessId} has not been found.");
            }

            if (status == HttpStatusCode.PreconditionFailed)
            {
                if (attempt >= maxAttempts)
                {
                    throw new ConflictException($"The machine access {machineAccessId} was changed concurrently; reload and retry.");
                }

                // The read decides: another hash means another reset won, the same one a write to another field.
                continue;
            }

            pending?.MarkWritten();
            return true;
        }
    }

    public async Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = repository.Key($"{project}.access");

        var machineAccess = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess?.HashedSecret is not null)
        {
            return new ApiKeyHashEntry(machineAccess.HashedSecret, machineAccess.Scopes ?? [], machineAccess.CredentialKind);
        }

        return null;
    }

    public async Task<bool> DeleteAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = repository.Key($"{project}.access");

        var machineAccess = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess?.HashedSecret is not null)
        {
            using var response = await repository.Container.PatchItemStreamAsync(
                machineAccessId,
                partitionKey,
                [PatchOperation.Set<object?>($"/{nameof(MachineAccessEntity.HashedSecret)}", null)]);

            // 404: deleted since the read, so there is no hash left to clear.
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
            }
        }

        return true;
    }

    private static async Task<HttpStatusCode> PatchHashAsync(
        OrganizationContainer repository,
        string machineAccessId,
        PartitionKey partitionKey,
        string newHashedSecret,
        string? eTag)
    {
        using var response = await repository.Container.PatchItemStreamAsync(
            machineAccessId,
            partitionKey,
            [PatchOperation.Set($"/{nameof(MachineAccessEntity.HashedSecret)}", newHashedSecret)],
            new PatchItemRequestOptions { IfMatchEtag = eTag, EnableContentResponseOnWrite = false });

        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed))
        {
            response.EnsureSuccessStatusCode();
        }

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> PatchHashWithRecordAsync(
        OrganizationContainer repository,
        string machineAccessId,
        PartitionKey partitionKey,
        string newHashedSecret,
        string? eTag,
        PendingAccessAudit pending)
    {
        using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
            .PatchItem(
                machineAccessId,
                [PatchOperation.Set($"/{nameof(MachineAccessEntity.HashedSecret)}", newHashedSecret)],
                new TransactionalBatchPatchItemRequestOptions { IfMatchEtag = eTag })
            .CreateItem(pending.Record)
            .ExecuteAsync();

        if (response.Failed(HttpStatusCode.NotFound) || response.Failed(HttpStatusCode.PreconditionFailed))
        {
            return response.StatusCode;
        }

        response.ThrowIfFailed($"Replacing the API key of the machine access {machineAccessId}");
        return HttpStatusCode.OK;
    }
}
