using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Accessing;

public class CosmosAccessService : IAccessService
{
    private const string MAIN_PARTITION_KEY = "access";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IContainerFactory _containerFactory;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMachineAccessService? _machineAccessService;

    public CosmosAccessService(IContainerRepositoryProvider repositoryProvider,
        IContainerFactory containerFactory,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMachineAccessService? machineAccessService = null)
    {
        _repositoryProvider = repositoryProvider;
        _machineAccessService = machineAccessService;
        _containerFactory = containerFactory;
        _serializerOptions = serializerOptions.Value;
    }

    private IMachineAccessService MachineAccessService =>
        _machineAccessService ?? throw new InvalidOperationException("IMachineAccessService is not registered. Please register it in the DI container.");

    public async Task<Account?> GetAccountAsync(string id)
    {
        var repository = _repositoryProvider.GetCore();
        var account = await repository.Container.TryReadItemAsync(
            id,
            new PartitionKey(MAIN_PARTITION_KEY),
            stream => stream.DeserializeSystemTextJson<AccountEntity>(_serializerOptions));
        return account is not null
            ? account.ToAccount()
            : null;
    }

    public async Task<Account> CreateAccountAsync(AccountCreate model)
    {
        var name = model.Name.Trim();
        var userName = model.UserName.Trim();
        var accountId = model.IdentityId.Trim();
        var account = await GetAccountAsync(accountId);

        if (account is not null)
        {
            throw new ConflictException($"The account '{userName}#{accountId}' already exists.", ErrorCodes.AccountExists);
        }

        var point = await CreateAccessPointAsync(new AccessPointCreate
        {
            OwnerId = accountId,
            Organization = model.Organization,
            Project = model.Project,
        });

        var accountEntity = new AccountEntity
        {
            Id = accountId,
            UserName = userName,
            Name = name,
            AccessMap = new()
            {
                { model.Organization, AccountRole.Owner },
                { point.Key, AccountRole.Owner },
            },
        };

        var repository = _repositoryProvider.GetCore();
        var response = await repository.Container.CreateItemAsync(accountEntity, new PartitionKey(MAIN_PARTITION_KEY));
        return response.Resource.ToAccount();
    }

    public async Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
    {
        var account = await GetAccountAsync(accountId);

        if (account is null)
        {
            return AccountRole.None;
        }

        return account.AccessMap.GetValueOrDefault(accessPointKey, AccountRole.None);
    }

    public async Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId)
    {
        var account = await GetAccountAsync(accountId);

        if (account is null)
        {
            throw new NotFoundException($"The account '{accountId}' doesn't exist.");
        }

        var accessPointIds = account.AccessMap
            .Where(pair => pair.Value >= AccountRole.Administrator)
            .Select(pair => $"apt.{pair.Key}")
            .ToList();

        if (accessPointIds.Count == 0)
        {
            return [];
        }

        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT * FROM ap WHERE ARRAY_CONTAINS(@ids, ap.id)");
        query.WithParameter("@ids", accessPointIds);
        using var iterator = repository.Container.GetItemQueryIterator<AccessPointEntity>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(MAIN_PARTITION_KEY),
            });

        var resultList = new List<AccessPointEntity>();

        while (iterator.HasMoreResults)
        {
            resultList.AddRange(await iterator.ReadNextAsync());
        }

        return resultList.Select(entity => entity.ToAccessPoint());
    }

    public async Task<AccessPoint?> GetAccessPointAsync(string key)
    {
        var accessPointId = $"apt.{key}";
        var repository = _repositoryProvider.GetCore();
        var accessPoint = await repository.Container.TryReadItemAsync(
            accessPointId,
            new PartitionKey(MAIN_PARTITION_KEY),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));
        return accessPoint is not null
            ? accessPoint.ToAccessPoint()
            : null;
    }

    public async Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model)
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        var organizationAccessPoint = await GetAccessPointAsync(model.Organization);

        if (organizationAccessPoint is not null
            && (!organizationAccessPoint.AccessMap.TryGetValue(model.OwnerId, out var role)
            || role != AccountRole.Owner))
        {
            throw new AccessDeniedException($"The account '{model.OwnerId}' doesn't have owner rights to the organization '{model.Organization}'.");
        }

        if (organizationAccessPoint is null)
        {
            await _containerFactory.CreateContainerIfNotExistsAsync($"org.{model.Organization}");

            var organizationAccessPointEntity = new AccessPointEntity
            {
                Key = model.Organization,
                AccessMap = new() { { model.OwnerId, AccountRole.Owner } },
            };
            await repository.Container.CreateItemAsync(organizationAccessPointEntity, partitionKey);
        }

        var accessPointKey = $"{model.Organization}.{model.Project}";
        var accessPoint = await GetAccessPointAsync(accessPointKey);

        if (accessPoint is not null)
        {
            throw new ConflictException($"The access point '{accessPointKey}' already exists.", ErrorCodes.AccessPointExists);
        }

        var accessPointEntity = new AccessPointEntity
        {
            Key = accessPointKey,
            AccessMap = new() { { model.OwnerId, AccountRole.Owner } },
        };

        var response = await repository.Container.CreateItemAsync(accessPointEntity, partitionKey);
        var account = await GetAccountAsync(model.OwnerId);

        if (account is not null)
        {
            account = account with
            {
                AccessMap = new Dictionary<string, AccountRole>(account.AccessMap)
                {
                    [model.Organization] = AccountRole.Owner,
                    [accessPointKey] = AccountRole.Owner,
                },
            };
            await repository.Container.UpsertItemAsync(account.ToEntity(), partitionKey);
        }

        return response.Resource.ToAccessPoint();
    }

    public async Task<bool> GrantPermissionAsync(Permission model)
    {
        if (model.Role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be granted, revoke the permission instead.", nameof(model));
        }

        await EnsureCanManageAsync(model);

        var accessPoint = await GetAccessPointAsync(model.AccessPointKey)
            ?? throw new NotFoundException($"The access point '{model.AccessPointKey}' doesn't exist.");
        var account = await GetAccountAsync(model.AccountId)
            ?? throw new NotFoundException($"The target account '{model.AccountId}' doesn't exist.");

        // Grant only raises a role. Lowering one is a revoke followed by a grant.
        if (accessPoint.AccessMap.TryGetValue(model.AccountId, out var accountRole)
            && accountRole >= model.Role)
        {
            return false;
        }

        await SaveMembershipAsync(accessPoint, account, model.Role);
        return true;
    }

    public async Task<bool> RevokePermissionAsync(Permission model)
    {
        await EnsureCanManageAsync(model);

        var accessPoint = await GetAccessPointAsync(model.AccessPointKey)
            ?? throw new NotFoundException($"The access point '{model.AccessPointKey}' doesn't exist.");
        var account = await GetAccountAsync(model.AccountId)
            ?? throw new NotFoundException($"The target account '{model.AccountId}' doesn't exist.");

        if (!accessPoint.AccessMap.TryGetValue(model.AccountId, out var accountRole)
            || accountRole < model.Role)
        {
            return false;
        }

        if (accountRole > model.Role)
        {
            throw new ConflictException(
                $"The target account '{model.AccountId}' has the higher role {accountRole} on '{model.AccessPointKey}', revoke that role instead.",
                ErrorCodes.ElevatedRole);
        }

        if (accountRole == AccountRole.Owner
            && accessPoint.AccessMap.Count(pair => pair.Value == AccountRole.Owner) <= 1)
        {
            throw new ConflictException(
                $"The account '{model.AccountId}' is the last owner of '{model.AccessPointKey}'.",
                ErrorCodes.LastOwner);
        }

        await SaveMembershipAsync(accessPoint, account, null);
        return true;
    }

    public async Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var query = new QueryDefinition("SELECT * FROM ma");

        using var iterator = repository.Container.GetItemQueryIterator<MachineAccessEntity>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey($"{project}.access"),
            });

        var resultList = new List<MachineAccessEntity>();

        while (iterator.HasMoreResults)
        {
            resultList.AddRange(await iterator.ReadNextAsync());
        }

        return resultList.Select(entity => entity.ToMachineAccess());
    }

    public async Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var machineAccess = await repository.Container.TryReadItemAsync(
            id,
            new PartitionKey($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));
        return machineAccess is not null
            ? machineAccess.ToMachineAccess()
            : null;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(model.Organization);
        var partitionKeyValue = $"{model.Project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);

        var machineAccess = await MachineAccessService.CreateMachineAccessAsync(model);

        var accessKey = machineAccess.AccessKey;
        var maskedKey = !string.IsNullOrEmpty(accessKey) && accessKey.Length >= 3
            ? $"{accessKey[..3]}***"
            : "***";

        // Read the partial entity that CosmosMergedApiKeyHashStore may have created
        // (it stores HashedSecret before the full entity exists).
        var existing = await repository.Container.TryReadItemAsync(
            machineAccess.Id,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        var entity = new MachineAccessEntity
        {
            PartitionKey = partitionKeyValue,
            Id = machineAccess.Id,
            ObjectId = machineAccess.ObjectId,
            AccessKey = maskedKey,
            Scope = machineAccess.Scope,
            AnnotationKey = machineAccess.AnnotationKey,
            CredentialKind = machineAccess.CredentialKind,
            CertificateThumbprint = machineAccess.CertificateThumbprint,
            HashedSecret = existing?.HashedSecret,
        };

        try
        {
            await repository.Container.UpsertItemAsync(entity, partitionKey);
        }
        catch
        {
            // Compensate: remove the hash entity stored by the machine access service
            try
            {
                await MachineAccessService.DeleteMachineAccessAsync(machineAccess, model.Organization, model.Project);
            }
            catch
            {
                // Best-effort compensation.
            }

            throw;
        }

        return machineAccess;
    }

    public async Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");
        var machineAccess = await repository.Container.TryReadItemAsync(
            appId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess is null)
        {
            throw new NotFoundException($"The machine access {appId} has not been found.");
        }

        var accessKey = await MachineAccessService.ResetMachineAccessAsync(machineAccess.ToMachineAccess(), organization, project);

        if (accessKey is null)
        {
            throw new InvalidOperationException($"The machine access {appId} reset failed.");
        }

        var maskedKey = !string.IsNullOrEmpty(accessKey) && accessKey.Length >= 3
            ? $"{accessKey[..3]}***"
            : "***";
        machineAccess = machineAccess with { AccessKey = maskedKey };
        await repository.Container.UpsertItemAsync(machineAccess, partitionKey);

        var result = machineAccess.ToMachineAccess();
        return result with { AccessKey = accessKey };
    }

    public async Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");
        var machineAccess = await repository.Container.TryReadItemAsync(
            appId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess is null)
        {
            // Already deleted — idempotent success.
            return false;
        }

        // Delete the credential first (hash entity or identity provider application; idempotent).
        await MachineAccessService.DeleteMachineAccessAsync(machineAccess.ToMachineAccess(), organization, project);

        // Delete the machine access entity.
        try
        {
            await repository.Container.DeleteItemAsync<MachineAccessEntity>(appId, partitionKey);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already deleted between our read and delete — idempotent.
            return false;
        }

        return true;
    }

    public async Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var response = await repository.Container.ReadContainerStreamAsync();

        if (response.StatusCode is HttpStatusCode.NotFound
            || response.Content is null)
        {
            return false;
        }

        var machineAccess = await repository.Container.TryReadItemAsync(
            appId,
            new PartitionKey($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));
        return machineAccess is not null;
    }

    // Grant and revoke need Administrator on the access point; only an Owner may grant or revoke Owner.
    private async Task EnsureCanManageAsync(Permission model)
    {
        var creator = await GetAccountAsync(model.CreatedById);
        var creatorRole = creator?.AccessMap.GetValueOrDefault(model.AccessPointKey, AccountRole.None) ?? AccountRole.None;

        if (creatorRole < AccountRole.Administrator
            || (model.Role == AccountRole.Owner && creatorRole != AccountRole.Owner))
        {
            throw new AccessDeniedException($"The account '{model.CreatedById}' can't manage the role {model.Role} on '{model.AccessPointKey}'.");
        }
    }

    // The membership is stored twice: in the access point and in the account. A null role removes it.
    private async Task SaveMembershipAsync(AccessPoint accessPoint, Account account, AccountRole? role)
    {
        var pointMap = new Dictionary<string, AccountRole>(accessPoint.AccessMap);
        var accountMap = new Dictionary<string, AccountRole>(account.AccessMap);

        if (role is { } newRole)
        {
            pointMap[account.Id] = newRole;
            accountMap[accessPoint.Key] = newRole;
        }
        else
        {
            pointMap.Remove(account.Id);
            accountMap.Remove(accessPoint.Key);
        }

        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        await repository.Container.UpsertItemAsync((accessPoint with { AccessMap = pointMap }).ToEntity(), partitionKey);
        await repository.Container.UpsertItemAsync((account with { AccessMap = accountMap }).ToEntity(), partitionKey);
    }
}
