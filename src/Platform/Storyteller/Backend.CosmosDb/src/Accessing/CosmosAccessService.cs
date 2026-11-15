using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Accessing;

// Every change writes its access audit record in the transactional batch of the change: core "access" for memberships
// and access points, the machine's partition of organizations for machines (docs/Platform/Storyteller/access-audit.md).
public class CosmosAccessService : IAccessService
{
    private const string MAIN_PARTITION_KEY = "access";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMachineAccessService? _machineAccessService;
    private readonly AccessAuditRecords _audit;

    public CosmosAccessService(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMachineAccessService? machineAccessService = null,
        AccessAuditRecords? audit = null)
    {
        _repositoryProvider = repositoryProvider;
        _machineAccessService = machineAccessService;
        _serializerOptions = serializerOptions.Value;
        _audit = audit ?? AccessAuditRecords.Default;
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

    public async Task<Account> CreateAccountAsync(AccountCreate model, AccessCaller caller)
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

        var accountEntity = new AccountEntity
        {
            Id = accountId,
            UserName = userName,
            Name = name,
            AccessMap = new Dictionary<string, AccountRole>(),
        };

        // An account without a project has no access, so nothing to record.
        if (!hasOrganization)
        {
            var repository = _repositoryProvider.GetCore();
            var response = await repository.Container.CreateItemAsync(accountEntity, new PartitionKey(MAIN_PARTITION_KEY));
            return response.Resource.ToAccount();
        }

        // The account is created in the batch of its access points, so neither exists without the other.
        var (_, created) = await CreateAccessPointsAsync(
            new AccessPointCreate
            {
                OwnerId = accountId,
                Organization = model.Organization!,
                Project = model.Project!,
                ProjectDescription = model.ProjectDescription,
            },
            caller,
            accountEntity);
        return created!.ToAccount();
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

    public async Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model, AccessCaller caller)
    {
        var (point, _) = await CreateAccessPointsAsync(model, caller, newAccount: null);
        return point;
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(string accountId)
    {
        var account = await GetAccountAsync(accountId);

        if (account is null)
        {
            throw new NotFoundException($"The account '{accountId}' doesn't exist.");
        }

        // A project key has a dot, an organization key has none.
        var roles = account.AccessMap
            .Where(pair => pair.Key.Contains('.') && pair.Value > AccountRole.None)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (roles.Count == 0)
        {
            return [];
        }

        // Only the key and the description: the members and the policies of the projects stay unread.
        // A key whose access point is missing finds no document and is left out.
        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT ap.Key, ap.Description FROM ap WHERE ARRAY_CONTAINS(@ids, ap.id)")
            .WithParameter("@ids", roles.Keys.Select(key => $"apt.{key}").ToList());
        using var iterator = repository.Container.GetItemQueryIterator<ProjectDescriptionRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(MAIN_PARTITION_KEY) });

        var projects = new List<ProjectSummary>();

        while (iterator.HasMoreResults)
        {
            foreach (var project in await iterator.ReadNextAsync())
            {
                if (roles.TryGetValue(project.Key, out var role))
                {
                    projects.Add(new ProjectSummary { Key = project.Key, Role = role, Description = project.Description });
                }
            }
        }

        return projects
            .OrderBy(project => project.Key, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<AccessPoint> UpdateAccessPointAsync(string key, AccessPointUpdate model, AccessCaller caller)
    {
        if (!key.Contains('.'))
        {
            throw new ArgumentException($"The access point '{key}' is an organization, only a project has a description.", nameof(key));
        }

        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        var description = NormalizeDescription(model.Description);

        // A concurrent write to the project (a membership, a policy) only moves the ETag, so one retry suffices.
        // The stored entity is replaced with only the description changed, so the members and the policies stay.
        for (var attempt = 1; ; attempt++)
        {
            var (entity, eTag) = await repository.Container.TryReadItemWithETagAsync(
                $"apt.{key}",
                partitionKey,
                stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));

            if (entity is null)
            {
                throw new NotFoundException($"The project '{key}' doesn't exist.");
            }

            var updated = entity with { Description = description };
            var batch = repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(updated.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = eTag });

            if (!string.Equals(entity.Description, description, StringComparison.Ordinal))
            {
                batch.CreateItem(_audit.ForAccessPoint(key, new AccessAuditChange(AccessAuditAction.AccessPointUpdated, caller)
                {
                    OldValue = AccessAuditValues.Text(entity.Description),
                    NewValue = AccessAuditValues.Text(description),
                }));
            }

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return updated.ToAccessPoint();
            }

            if (!response.Failed(HttpStatusCode.PreconditionFailed))
            {
                response.ThrowIfFailed($"Updating the project '{key}'");
            }

            if (attempt > 1)
            {
                throw new ConflictException($"The project '{key}' was changed concurrently, try again.", ErrorCodes.Conflict);
            }
        }
    }

    public async Task<bool> GrantPermissionAsync(Permission model, AccessCaller caller)
    {
        if (model.Role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be granted, revoke the permission instead.", nameof(model));
        }

        var (changed, _) = await UpdateMembershipAsync(model.AccessPointKey, model.AccountId, AccessAuditAction.MemberGranted, caller, (point, _) =>
        {
            EnsureCanManage(point, caller.Actor.Id, model.Role);

            // Grant only raises a role. Lowering one is a revoke followed by a grant, or a member role update.
            return point.AccessMap.TryGetValue(model.AccountId, out var accountRole) && accountRole >= model.Role
                ? MembershipDecision.Keep
                : MembershipDecision.Set(model.Role);
        });

        return changed;
    }

    public async Task<bool> RevokePermissionAsync(Permission model, AccessCaller caller)
    {
        var (changed, _) = await UpdateMembershipAsync(model.AccessPointKey, model.AccountId, AccessAuditAction.MemberRevoked, caller, (point, _) =>
        {
            EnsureCanManage(point, caller.Actor.Id, model.Role);

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

    public async Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller)
    {
        if (role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be set, remove the member instead.", nameof(role));
        }

        var actorId = caller.Actor.Id;
        var (_, resultRole) = await UpdateMembershipAsync(accessPointKey, accountId, AccessAuditAction.MemberRoleChanged, caller, (point, _) =>
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

    public async Task RemoveMemberAsync(string accessPointKey, string accountId, AccessCaller caller)
    {
        var actorId = caller.Actor.Id;
        await UpdateMembershipAsync(accessPointKey, accountId, AccessAuditAction.MemberRemoved, caller, (point, _) =>
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

    public async Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller, string? detail = null)
    {
        if (role == AccountRole.None || !Enum.IsDefined(role))
        {
            throw new ArgumentException($"The role {role} can't be joined with.", nameof(role));
        }

        var (_, resultRole) = await UpdateMembershipAsync(
            accessPointKey,
            accountId,
            AccessAuditAction.MemberJoined,
            caller,
            (point, _) => point.AccessMap.TryGetValue(accountId, out var currentRole) && currentRole >= role
                ? MembershipDecision.Keep
                : MembershipDecision.Set(role),
            detail);

        return resultRole ?? role;
    }

    public async Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        // The machines' partition also holds their certificate records, the shared certificates and the access audit
        // records of both; only the machines are listed.
        var query = new QueryDefinition(
                "SELECT * FROM ma WHERE NOT STARTSWITH(ma.id, @audit) AND NOT STARTSWITH(ma.id, @certificate) AND NOT STARTSWITH(ma.id, @shared)")
            .WithParameter("@audit", AccessAuditEntity.IdPrefix)
            .WithParameter("@certificate", MachineCertificateEntity.IdPrefix)
            .WithParameter("@shared", CosmosSharedCertificateStore.IdPrefix);

        using var iterator = repository.Container.GetItemQueryIterator<MachineAccessEntity>(
            query,
            requestOptions: repository.QueryOptions($"{project}.access"));

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
            repository.Key($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));
        return machineAccess is not null
            ? machineAccess.ToMachineAccess()
            : null;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model, AccessCaller caller)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(model.Organization);
        var partitionKeyValue = $"{model.Project}.access";
        var partitionKey = repository.Key(partitionKeyValue);

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

        // Every provider returns exactly the requested scopes, so the stored list equals the request.
        var entity = new MachineAccessEntity
        {
            PartitionKey = partitionKeyValue,
            Organization = repository.Organization,
            Id = machineAccess.Id,
            ObjectId = machineAccess.ObjectId,
            AccessKey = maskedKey,
            Scopes = machineAccess.Scopes.ToList(),
            AnnotationKey = machineAccess.AnnotationKey,
            CredentialKind = machineAccess.CredentialKind,
            CertificateThumbprint = machineAccess.CertificateThumbprint,
            CertificateLifetimeDays = machineAccess.CertificateLifetimeDays,
            NotAfter = machineAccess.CertificateNotAfter,
            UsesSharedCertificate = machineAccess.UsesSharedCertificate,
            FederationRules = machineAccess.FederationRules?.ToList(),
            HashedSecret = existing?.HashedSecret,
        };

        // The machine and its record in one batch; the credential leaves only after both are stored.
        var record = _audit.ForProject(repository.Organization, model.Project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.MachineCreated, caller)
        {
            Target = AccessAuditTarget.Machine(entity.Id),
            NewValue = MachineValue(entity),
            Detail = entity.FederationRules is { Count: > 0 } rules ? $"Federation rules: {AccessAuditValues.FederationRules(rules)}" : null,
        });

        try
        {
            using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
                .UpsertItem(entity)
                .CreateItem(record)
                .ExecuteAsync();
            response.ThrowIfFailed($"Storing the machine access {entity.Id}");
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

    public async Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId, AccessCaller caller)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);
        var machineAccess = await repository.Container.TryReadItemAsync(
            appId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess is null)
        {
            throw new NotFoundException($"The machine access {appId} has not been found.");
        }

        // The record of the reset, for the store that commits the new credential: the hash store patches an API key's
        // new hash in one batch with it. Never the key: only which credential was replaced.
        var record = _audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.MachineReset, caller)
        {
            Target = AccessAuditTarget.Machine(appId),
            NewValue = machineAccess.CredentialKind.ToString(),
            Detail = machineAccess.CredentialKind.HasApiKey() ? "The API key was replaced." : "The client secret was replaced.",
        });

        string? accessKey;
        bool isRecorded;

        using (var scope = PendingAccessAudit.Begin(repository.Organization, partitionKeyValue, appId, record))
        {
            accessKey = await MachineAccessService.ResetMachineAccessAsync(machineAccess.ToMachineAccess(), organization, project);
            isRecorded = scope.Pending.IsWritten;
        }

        // Only a delete removes a hash, and it does so before it deletes the document.
        var deletedMessage = $"The machine access {appId} was deleted during its reset.";

        if (accessKey is null)
        {
            if (machineAccess.HashedSecret is not null)
            {
                throw new NotFoundException(deletedMessage);
            }

            // A machine of a kind with an API key had none at the read: a delete removed it before the document, or the
            // compensation of a failed create did. No retry brings a key back. Any other kind got no secret from its
            // identity provider, which is no client error.
            throw machineAccess.CredentialKind.HasApiKey()
                ? new NotFoundException($"The machine access {appId} has no API key to reset; it is being deleted, or its creation failed. Delete it and create a new one.")
                : new InvalidOperationException($"The machine access {appId} reset failed.");
        }

        var maskedKey = !string.IsNullOrEmpty(accessKey) && accessKey.Length >= 3
            ? $"{accessKey[..3]}***"
            : "***";

        if (!isRecorded)
        {
            // Nothing below wrote the record: the identity provider replaced the secret, outside any transaction of
            // ours. The mask is patched with the record, always, and the secret is returned only after both are
            // stored; a failed batch answers an error and the secret never leaves. Only the reset writes the mask, so
            // no ETag.
            using var batch = await repository.Container.CreateTransactionalBatch(partitionKey)
                .PatchItem(appId, [PatchOperation.Set($"/{nameof(MachineAccessEntity.AccessKey)}", maskedKey)])
                .CreateItem(record)
                .ExecuteAsync();

            if (batch.Failed(HttpStatusCode.NotFound))
            {
                throw new NotFoundException(deletedMessage);
            }

            batch.ThrowIfFailed($"Recording the reset of the machine access {appId}");
        }

        // The copy read above is stale (the new hash, a concurrent renewal), so the record comes back fresh. An API key
        // keeps its mask (2s.***), so nothing is written after the hash: a failed write here would answer 5xx for a
        // key that already works. Only a changed mask of an API key (an old record) is patched, without an ETag, as
        // only the reset writes this field. Neither a read nor a patch brings back a machine deleted meanwhile.
        using var response = isRecorded && !string.Equals(machineAccess.AccessKey, maskedKey, StringComparison.Ordinal)
            ? await repository.Container.PatchItemStreamAsync(
                appId,
                partitionKey,
                [PatchOperation.Set($"/{nameof(MachineAccessEntity.AccessKey)}", maskedKey)],
                new PatchItemRequestOptions { EnableContentResponseOnWrite = true })
            : await repository.Container.ReadItemStreamAsync(appId, partitionKey);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException(deletedMessage);
        }

        response.EnsureSuccessStatusCode();

        // The stored record after the reset, with whatever landed meanwhile. A delete that already removed the new
        // hash leaves a key that never works, so the reset is not answered as a success.
        var stored = response.Content.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions)!;

        if (machineAccess.HashedSecret is not null && stored.HashedSecret is null)
        {
            throw new NotFoundException(deletedMessage);
        }

        return stored.ToMachineAccess() with { AccessKey = accessKey };
    }

    public async Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId, AccessCaller caller)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);
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

        // Delete the machine access entity, with its record: a delete that fails here is recorded when it is retried.
        using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
            .DeleteItem(appId)
            .CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.MachineDeleted, caller)
            {
                Target = AccessAuditTarget.Machine(appId),
                OldValue = MachineValue(machineAccess),
            }))
            .ExecuteAsync();

        if (response.Failed(HttpStatusCode.NotFound))
        {
            // Already deleted between our read and delete — idempotent.
            return false;
        }

        response.ThrowIfFailed($"Deleting the machine access {appId}");
        return true;
    }

    public async Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId)
    {
        // A missing organization has no machine, so the point read with the full key answers it too.
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var machineAccess = await repository.Container.TryReadItemAsync(
            appId,
            repository.Key($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));
        return machineAccess is not null;
    }

    public async Task<MachineAccess> SetMachineFederationRulesAsync(string organization, string project, string id, IReadOnlyList<FederationRule> rules, AccessCaller caller)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);

        // A concurrent write to the machine (a reset, a certificate renewal) only moves the ETag, so one retry suffices.
        for (var attempt = 1; ; attempt++)
        {
            var entity = await repository.Container.TryReadItemAsync(
                id,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

            if (entity is null)
            {
                throw new NotFoundException($"The machine access {id} has not been found.");
            }

            if (entity.CredentialKind != MachineCredentialKind.Federated)
            {
                throw new InvalidInputException(FederationValidation.RulesOnOtherKindMessage, ErrorCodes.MachineNotFederated);
            }

            var updated = entity with { FederationRules = rules.ToList() };
            var oldRules = AccessAuditValues.FederationRules(entity.FederationRules);
            var newRules = AccessAuditValues.FederationRules(updated.FederationRules);
            var batch = repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(updated.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = entity.ETag });

            if (!string.Equals(oldRules, newRules, StringComparison.Ordinal))
            {
                batch.CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.MachineFederationRulesChanged, caller)
                {
                    Target = AccessAuditTarget.Machine(id),
                    OldValue = oldRules,
                    NewValue = newRules,
                }));
            }

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return updated.ToMachineAccess();
            }

            if (response.Failed(HttpStatusCode.NotFound))
            {
                throw new NotFoundException($"The machine access {id} has not been found.");
            }

            if (!response.Failed(HttpStatusCode.PreconditionFailed))
            {
                response.ThrowIfFailed($"Replacing the federation rules of the machine access {id}");
            }

            if (attempt > 1)
            {
                throw new ConflictException($"The machine access {id} was changed concurrently; reload and retry.");
            }
        }
    }

    public async Task<IReadOnlyList<string>> FindMachinesUsingIssuerAsync(string organization, string project, string issuerName, int max)
    {
        if (max < 1)
        {
            return [];
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var query = new QueryDefinition(
                "SELECT TOP @max VALUE ma.id FROM ma WHERE EXISTS(SELECT VALUE r FROM r IN ma.FederationRules WHERE r.Issuer = @issuer)")
            .WithParameter("@max", max)
            .WithParameter("@issuer", issuerName);
        var ids = new List<string>();

        try
        {
            using var iterator = repository.Container.GetItemQueryIterator<string>(
                query,
                requestOptions: repository.QueryOptions($"{project}.access"));

            while (iterator.HasMoreResults && ids.Count < max)
            {
                ids.AddRange(await iterator.ReadNextAsync());
            }
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // No container organizations (a missing organization is only an empty partition): no machines.
            return [];
        }

        return ids.Take(max).ToList();
    }

    // The rule of view descriptions: trimmed, and a blank one is none. No length rule.
    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
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
    // The record of the change is the third item of the batch, so a membership never changes without it.
    private async Task<(bool Changed, AccountRole? Role)> UpdateMembershipAsync(
        string accessPointKey,
        string accountId,
        AccessAuditAction action,
        AccessCaller caller,
        Func<AccessPoint, Account, MembershipDecision> decide,
        string? detail = null)
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

            var record = _audit.ForAccessPoint(accessPointKey, new AccessAuditChange(action, caller)
            {
                Target = AccessAuditTarget.Account(accountId, AccountName(accountEntity)),
                OldValue = AccessAuditValues.Role(point.AccessMap.TryGetValue(accountId, out var oldRole) ? oldRole : null),
                NewValue = AccessAuditValues.Role(decision.Role),
                Detail = detail,
            });

            using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(pointEntity.Id, pointEntity with { AccessMap = pointMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = pointETag })
                .ReplaceItem(accountEntity.Id, accountEntity with { AccessMap = accountMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = accountETag })
                .CreateItem(record)
                .ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return (true, decision.Role);
            }

            if (!response.Failed(HttpStatusCode.PreconditionFailed))
            {
                response.ThrowIfFailed($"Updating the membership of '{accountId}' in '{accessPointKey}'");
            }

            if (attempt >= maxAttempts)
            {
                throw new ConflictException(
                    $"The membership of '{accountId}' in '{accessPointKey}' was changed concurrently, try again.",
                    ErrorCodes.Conflict);
            }
        }
    }

    // The access point of the project, and of its organization when it is new, with the owner's account (created when
    // newAccount is given, otherwise replaced with its ETag when it exists) and a record per access point created, in
    // one batch of core "access": a failure leaves none of them. A concurrent creation is read again once.
    private async Task<(AccessPoint Point, AccountEntity? Account)> CreateAccessPointsAsync(
        AccessPointCreate model,
        AccessCaller caller,
        AccountEntity? newAccount)
    {
        const int maxAttempts = 2;
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);

        for (var attempt = 1; ; attempt++)
        {
            var organizationAccessPoint = await GetAccessPointAsync(model.Organization);

            // Only new names are checked: projects of an organization created before the rules can still be added.
            // A dotted name is never an organization, even when it matches an existing project.
            if (organizationAccessPoint is null || model.Organization.Contains('.'))
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

            var accessPointKey = $"{model.Organization}.{model.Project}";

            if (await GetAccessPointAsync(accessPointKey) is not null)
            {
                throw new ConflictException($"The access point '{accessPointKey}' already exists.", ErrorCodes.AccessPointExists);
            }

            var (account, accountETag) = newAccount is not null
                ? (newAccount, null)
                : await repository.Container.TryReadItemWithETagAsync(
                    model.OwnerId,
                    partitionKey,
                    stream => stream.DeserializeSystemTextJson<AccountEntity>(_serializerOptions));
            var owner = AccessAuditTarget.Account(model.OwnerId, account is null ? null : AccountName(account));
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);

            // An organization is its access point in core: its data lives in the shared container organizations, so
            // creating one creates no container.
            if (organizationAccessPoint is null)
            {
                batch.CreateItem(new AccessPointEntity
                {
                    Key = model.Organization,
                    AccessMap = new() { { model.OwnerId, AccountRole.Owner } },
                });
                batch.CreateItem(_audit.ForAccessPoint(model.Organization, new AccessAuditChange(AccessAuditAction.AccessPointCreated, caller)
                {
                    Target = owner,
                    NewValue = AccessAuditValues.Role(AccountRole.Owner),
                }));
            }

            var accessPointEntity = new AccessPointEntity
            {
                Key = accessPointKey,
                AccessMap = new() { { model.OwnerId, AccountRole.Owner } },
                Description = NormalizeDescription(model.ProjectDescription),
            };
            batch.CreateItem(accessPointEntity);
            batch.CreateItem(_audit.ForAccessPoint(accessPointKey, new AccessAuditChange(AccessAuditAction.AccessPointCreated, caller)
            {
                Target = owner,
                NewValue = AccessAuditValues.Role(AccountRole.Owner),
                Detail = AccessAuditValues.Text(accessPointEntity.Description),
            }));

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

                if (newAccount is not null)
                {
                    batch.CreateItem(account);
                }
                else
                {
                    batch.ReplaceItem(account.Id, account, new TransactionalBatchItemRequestOptions { IfMatchEtag = accountETag });
                }
            }

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return (accessPointEntity.ToAccessPoint(), account);
            }

            // 409: an access point (or the new account) was created meanwhile; 412: the owner's account changed.
            if (!response.Failed(HttpStatusCode.Conflict) && !response.Failed(HttpStatusCode.PreconditionFailed))
            {
                response.ThrowIfFailed($"Creating the access point '{accessPointKey}'");
            }

            if (newAccount is not null && response.Failed(HttpStatusCode.Conflict) && await GetAccountAsync(newAccount.Id) is not null)
            {
                throw new ConflictException($"The account '{newAccount.UserName}#{newAccount.Id}' already exists.", ErrorCodes.AccountExists);
            }

            if (attempt >= maxAttempts)
            {
                throw new ConflictException($"The access point '{accessPointKey}' was changed concurrently, try again.", ErrorCodes.Conflict);
            }
        }
    }

    private static string? AccountName(AccountEntity account)
    {
        return string.IsNullOrWhiteSpace(account.Name)
            ? string.IsNullOrWhiteSpace(account.UserName) ? null : account.UserName
            : account.Name;
    }

    private static string MachineValue(MachineAccessEntity machine)
    {
        return AccessAuditValues.Machine(machine.CredentialKind, machine.Scopes, machine.AnnotationKey, machine.UsesSharedCertificate, machine.CertificateThumbprint);
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

    private sealed record ProjectDescriptionRow
    {
        public required string Key { get; init; }

        public string? Description { get; init; }
    }
}
