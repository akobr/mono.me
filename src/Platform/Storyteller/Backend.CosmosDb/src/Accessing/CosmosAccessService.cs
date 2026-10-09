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

        var hasOrganization = !string.IsNullOrWhiteSpace(model.Organization);
        var hasProject = !string.IsNullOrWhiteSpace(model.Project);

        if (hasOrganization != hasProject)
        {
            throw new ArgumentException("The organization and the project are given together, or not at all.", nameof(model));
        }

        var accessMap = new Dictionary<string, AccountRole>();

        if (hasOrganization)
        {
            var point = await CreateAccessPointAsync(new AccessPointCreate
            {
                OwnerId = accountId,
                Organization = model.Organization!,
                Project = model.Project!,
            });
            accessMap[model.Organization!] = AccountRole.Owner;
            accessMap[point.Key] = AccountRole.Owner;
        }

        var accountEntity = new AccountEntity
        {
            Id = accountId,
            UserName = userName,
            Name = name,
            AccessMap = accessMap,
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

        // Only new names are checked: projects of an organization created before the rules can still be added.
        if (organizationAccessPoint is null)
        {
            NameRules.EnsureOrganizationName(model.Organization);
        }

        NameRules.EnsureProjectName(model.Project);

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

        var (changed, _) = await UpdateMembershipAsync(model.AccessPointKey, model.AccountId, (point, _) =>
        {
            EnsureCanManage(point, model.CreatedById, model.Role);

            // Grant only raises a role. Lowering one is a revoke followed by a grant, or a member role update.
            return point.AccessMap.TryGetValue(model.AccountId, out var accountRole) && accountRole >= model.Role
                ? MembershipDecision.Keep
                : MembershipDecision.Set(model.Role);
        });

        return changed;
    }

    public async Task<bool> RevokePermissionAsync(Permission model)
    {
        var (changed, _) = await UpdateMembershipAsync(model.AccessPointKey, model.AccountId, (point, _) =>
        {
            EnsureCanManage(point, model.CreatedById, model.Role);

            if (!point.AccessMap.TryGetValue(model.AccountId, out var accountRole)
                || accountRole < model.Role)
            {
                return MembershipDecision.Keep;
            }

            if (accountRole > model.Role)
            {
                throw new ConflictException(
                    $"The target account '{model.AccountId}' has the higher role {accountRole} on '{model.AccessPointKey}', revoke that role instead.",
                    ErrorCodes.ElevatedRole);
            }

            EnsureNotLastOwner(point, model.AccountId, accountRole);
            return MembershipDecision.Remove;
        });

        return changed;
    }

    public async Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId)
    {
        var point = await GetAccessPointAsync(accessPointKey)
            ?? throw new NotFoundException($"The access point '{accessPointKey}' doesn't exist.");

        if (point.AccessMap.GetValueOrDefault(actorId, AccountRole.None) < AccountRole.Administrator)
        {
            throw new AccessDeniedException($"The account '{actorId}' can't list the members of '{accessPointKey}'.");
        }

        var accounts = await GetAccountProfilesAsync(point.AccessMap.Keys);

        return point.AccessMap
            .Select(pair => new AccessPointMember
            {
                AccountId = pair.Key,
                UserName = accounts.GetValueOrDefault(pair.Key)?.UserName,
                Name = accounts.GetValueOrDefault(pair.Key)?.Name,
                Role = pair.Value,
            })
            .OrderByDescending(member => member.Role)
            .ThenBy(member => member.Name ?? member.UserName ?? member.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId)
    {
        if (role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be set, remove the member instead.", nameof(role));
        }

        var (_, resultRole) = await UpdateMembershipAsync(accessPointKey, accountId, (point, _) =>
        {
            var actorRole = point.AccessMap.GetValueOrDefault(actorId, AccountRole.None);

            if (actorRole < AccountRole.Administrator)
            {
                throw new AccessDeniedException($"The account '{actorId}' can't change roles on '{accessPointKey}'.");
            }

            if (!point.AccessMap.TryGetValue(accountId, out var currentRole))
            {
                throw new NotFoundException($"The account '{accountId}' is not a member of '{accessPointKey}'.", ErrorCodes.MemberNotFound);
            }

            if (string.Equals(actorId, accountId, StringComparison.Ordinal))
            {
                throw new ConflictException("Members can't change their own role.", ErrorCodes.SelfRoleChange);
            }

            if ((currentRole == AccountRole.Owner || role == AccountRole.Owner) && actorRole != AccountRole.Owner)
            {
                throw new AccessDeniedException($"Only an owner can change the role {AccountRole.Owner} on '{accessPointKey}'.");
            }

            if (currentRole == role)
            {
                return MembershipDecision.Keep;
            }

            EnsureNotLastOwner(point, accountId, currentRole);
            return MembershipDecision.Set(role);
        });

        var account = await GetAccountAsync(accountId);
        return new AccessPointMember
        {
            AccountId = accountId,
            UserName = account?.UserName,
            Name = account?.Name,
            Role = resultRole ?? role,
        };
    }

    public async Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId)
    {
        await UpdateMembershipAsync(accessPointKey, accountId, (point, _) =>
        {
            var isLeaving = string.Equals(actorId, accountId, StringComparison.Ordinal);
            var actorRole = point.AccessMap.GetValueOrDefault(actorId, AccountRole.None);

            if (!isLeaving && actorRole < AccountRole.Administrator)
            {
                throw new AccessDeniedException($"The account '{actorId}' can't remove members of '{accessPointKey}'.");
            }

            if (!point.AccessMap.TryGetValue(accountId, out var currentRole))
            {
                throw new NotFoundException($"The account '{accountId}' is not a member of '{accessPointKey}'.", ErrorCodes.MemberNotFound);
            }

            if (!isLeaving && currentRole == AccountRole.Owner && actorRole != AccountRole.Owner)
            {
                throw new AccessDeniedException($"Only an owner can remove an owner of '{accessPointKey}'.");
            }

            EnsureNotLastOwner(point, accountId, currentRole);
            return MembershipDecision.Remove;
        });
    }

    public async Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role)
    {
        if (role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be joined with.", nameof(role));
        }

        var (_, resultRole) = await UpdateMembershipAsync(accessPointKey, accountId, (point, _) =>
            point.AccessMap.TryGetValue(accountId, out var currentRole) && currentRole >= role
                ? MembershipDecision.Keep
                : MembershipDecision.Set(role));

        return resultRole ?? role;
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

    // Managing a role needs Administrator on the access point; only an Owner may grant or revoke Owner.
    // The access point's own map is the authority, read in the same optimistic transaction as the change.
    private static void EnsureCanManage(AccessPoint point, string actorId, AccountRole role)
    {
        var actorRole = point.AccessMap.GetValueOrDefault(actorId, AccountRole.None);

        if (actorRole < AccountRole.Administrator
            || (role == AccountRole.Owner && actorRole != AccountRole.Owner))
        {
            throw new AccessDeniedException($"The account '{actorId}' can't manage the role {role} on '{point.Key}'.");
        }
    }

    private static void EnsureNotLastOwner(AccessPoint point, string accountId, AccountRole currentRole)
    {
        if (currentRole == AccountRole.Owner
            && point.AccessMap.Count(pair => pair.Value == AccountRole.Owner) <= 1)
        {
            throw new ConflictException($"The account '{accountId}' is the last owner of '{point.Key}'.", ErrorCodes.LastOwner);
        }
    }

    // The membership is stored twice: in the access point and in the account. Both documents live in the
    // main partition, so one transactional batch replaces them together, guarded by their ETags.
    // A concurrent change of either document makes the batch fail with 412; the rule runs once more on fresh data.
    private async Task<(bool Changed, AccountRole? Role)> UpdateMembershipAsync(
        string accessPointKey,
        string accountId,
        Func<AccessPoint, Account, MembershipDecision> decide)
    {
        const int maxAttempts = 2;
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);

        for (var attempt = 1; ; attempt++)
        {
            var (pointEntity, pointETag) = await repository.Container.TryReadItemWithETagAsync(
                $"apt.{accessPointKey}",
                partitionKey,
                stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));
            var (accountEntity, accountETag) = await repository.Container.TryReadItemWithETagAsync(
                accountId,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<AccountEntity>(_serializerOptions));

            if (pointEntity is null)
            {
                throw new NotFoundException($"The access point '{accessPointKey}' doesn't exist.");
            }

            if (accountEntity is null)
            {
                throw new NotFoundException($"The target account '{accountId}' doesn't exist.");
            }

            var point = pointEntity.ToAccessPoint();
            var account = accountEntity.ToAccount();
            var decision = decide(point, account);

            if (!decision.IsChange)
            {
                return (false, point.AccessMap.TryGetValue(accountId, out var keptRole) ? keptRole : null);
            }

            var pointMap = new Dictionary<string, AccountRole>(point.AccessMap);
            var accountMap = new Dictionary<string, AccountRole>(account.AccessMap);

            if (decision.Role is { } newRole)
            {
                pointMap[accountId] = newRole;
                accountMap[accessPointKey] = newRole;
            }
            else
            {
                pointMap.Remove(accountId);
                accountMap.Remove(accessPointKey);
            }

            using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(pointEntity.Id, pointEntity with { AccessMap = pointMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = pointETag })
                .ReplaceItem(accountEntity.Id, accountEntity with { AccessMap = accountMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = accountETag })
                .ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return (true, decision.Role);
            }

            if (response.StatusCode != HttpStatusCode.PreconditionFailed)
            {
                throw new InvalidOperationException($"Updating the membership of '{accountId}' in '{accessPointKey}' failed with {(int)response.StatusCode}: {response.ErrorMessage}");
            }

            if (attempt >= maxAttempts)
            {
                throw new ConflictException(
                    $"The membership of '{accountId}' in '{accessPointKey}' was changed concurrently, try again.",
                    ErrorCodes.Conflict);
            }
        }
    }

    private async Task<Dictionary<string, AccountProfile>> GetAccountProfilesAsync(IEnumerable<string> accountIds)
    {
        var ids = accountIds.ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, AccountProfile>(StringComparer.Ordinal);
        }

        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT c.id, c.UserName, c.Name FROM c WHERE ARRAY_CONTAINS(@ids, c.id)")
            .WithParameter("@ids", ids);
        using var iterator = repository.Container.GetItemQueryIterator<AccountProfile>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(MAIN_PARTITION_KEY) });

        var profiles = new Dictionary<string, AccountProfile>(StringComparer.Ordinal);

        while (iterator.HasMoreResults)
        {
            foreach (var profile in await iterator.ReadNextAsync())
            {
                profiles[profile.Id] = profile;
            }
        }

        return profiles;
    }

    private readonly record struct MembershipDecision(bool IsChange, AccountRole? Role)
    {
        public static MembershipDecision Keep => new(false, null);

        public static MembershipDecision Remove => new(true, null);

        public static MembershipDecision Set(AccountRole role) => new(true, role);
    }

    private sealed record AccountProfile
    {
        [Newtonsoft.Json.JsonProperty("id")]
        public required string Id { get; init; }

        public string? UserName { get; init; }

        public string? Name { get; init; }
    }
}
