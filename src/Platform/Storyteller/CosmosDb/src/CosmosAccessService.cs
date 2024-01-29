using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Access.Models;
using Microsoft.Azure.Cosmos;
using Permission = _42.Platform.Storyteller.Access.Models.Permission;

namespace _42.Platform.Storyteller;

public class CosmosAccessService : IAccessService
{
    private const string MAIN_PARTITION_KEY = "AccessManagement";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IMachineAccessService _machineAccessService;
    private readonly IContainerFactory _containerFactory;

    public CosmosAccessService(
        IContainerRepositoryProvider repositoryProvider,
        IMachineAccessService machineAccessService,
        IContainerFactory containerFactory)
    {
        _repositoryProvider = repositoryProvider;
        _machineAccessService = machineAccessService;
        _containerFactory = containerFactory;
    }

    public async Task<Account?> GetAccountAsync(string key)
    {
        var accountId = $"act.{key}";
        var repository = _repositoryProvider.GetCore();
        var response = await repository.Container.ReadItemAsync<Account>(accountId, new PartitionKey(MAIN_PARTITION_KEY));
        return response.Resource;
    }

    public async Task<Account?> CreateAccountAsync(AccountCreate model)
    {
        var accountName = model.Name.Trim().ToLowerInvariant();
        var accountKey = model.Name.ToKey();
        var account = await GetAccountAsync(accountKey);

        if (account is not null)
        {
            throw new InvalidOperationException($"The account '{accountName}' already exists.");
        }

        var point = await CreateAccessPointAsync(new AccessPointCreate
        {
            OwnerKey = accountKey,
            Organization = model.Organization,
            Project = model.Project,
        });

        account = new Account
        {
            Key = accountKey,
            Name = accountName,
            AccessMap = new()
            {
                { model.Organization, AccountRole.Owner },
                { point.Key, AccountRole.Owner },
            },
        };

        var repository = _repositoryProvider.GetCore();
        var response = await repository.Container.CreateItemAsync(account, new PartitionKey(MAIN_PARTITION_KEY));
        return response.Resource;
    }

    public async Task<AccountRole> GetAccountRoleAsync(string accountKey, string accessPointKey)
    {
        var account = await GetAccountAsync(accountKey);

        if (account is null)
        {
            return AccountRole.None;
        }

        return account.AccessMap.TryGetValue(accessPointKey, out var role)
            ? role
            : AccountRole.None;
    }

    public async Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountKey)
    {
        var account = await GetAccountAsync(accountKey);

        if (account is null)
        {
            throw new InvalidOperationException($"The owner '{accountKey}' doesn't exist.");
        }

        var accessPointIds = account.AccessMap
            .Where(pair => pair.Value >= AccountRole.Administrator)
            .Select(pair => $"apt.{pair.Key}");

        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT * FROM ap WHERE ap.Id IN @ids");
        query.WithParameter("@ids", accessPointIds);
        using var iterator = repository.Container.GetItemQueryIterator<AccessPoint>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(MAIN_PARTITION_KEY),
            });

        var resultList = new List<AccessPoint>();

        while (iterator.HasMoreResults)
        {
            resultList.AddRange(await iterator.ReadNextAsync());
        }

        return resultList;
    }

    public async Task<AccessPoint?> GetAccessPointAsync(string key)
    {
        var accessPointId = $"apt.{key}";
        var repository = _repositoryProvider.GetCore();
        var response = await repository.Container.ReadItemAsync<AccessPoint>(accessPointId, new PartitionKey(MAIN_PARTITION_KEY));
        return response.Resource;
    }

    public async Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model)
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        var organizationAccessPoint = await GetAccessPointAsync(model.Organization);

        if (organizationAccessPoint is not null
            && (!organizationAccessPoint.AccessMap.TryGetValue(model.OwnerKey, out var role)
            || role != AccountRole.Owner))
        {
            throw new InvalidOperationException($"The account '{model.OwnerKey}' doesn't have owner rights to the organization.");
        }

        if (organizationAccessPoint is null)
        {
            await _containerFactory.CreateContainerIfNotExistsAsync(model.Organization);
            await _machineAccessService.CreateOrganizationAsync(model.Organization);

            organizationAccessPoint = new AccessPoint
            {
                Key = model.Organization,
                AccessMap = new() { { model.OwnerKey, AccountRole.Owner } },
            };
            await repository.Container.CreateItemAsync(organizationAccessPoint, partitionKey);
        }

        var accessPointKey = $"{model.Organization}.{model.Project}";
        var accessPoint = await GetAccessPointAsync(accessPointKey);

        if (accessPoint is not null)
        {
            throw new InvalidOperationException($"The access point '{accessPointKey}' already exists.");
        }

        accessPoint = new AccessPoint
        {
            Key = accessPointKey,
            AccessMap = new() { { model.OwnerKey, AccountRole.Owner } },
        };

        var response = await repository.Container.CreateItemAsync(accessPoint, partitionKey);
        var account = await GetAccountAsync(model.OwnerKey);

        if (account is not null)
        {
            account.AccessMap[model.Organization] = AccountRole.Owner;
            account.AccessMap[accessPointKey] = AccountRole.Owner;
            await repository.Container.UpsertItemAsync(account, partitionKey);
        }

        return response.Resource;
    }

    public async Task<bool> GrantPermissionAsync(Permission model)
    {
        if (model.Role == AccountRole.None)
        {
            throw new InvalidOperationException("The no role can't be allowed.");
        }

        var creator = await GetAccountAsync(model.CreatedByKey);
        var account = await GetAccountAsync(model.AccountKey);

        if (creator is null)
        {
            throw new InvalidOperationException($"The creator '{model.CreatedByKey}' doesn't exist.");
        }

        if (account is null)
        {
            throw new InvalidOperationException($"The target account '{model.AccountKey}' doesn't exist.");
        }

        if (!creator.AccessMap.TryGetValue(model.AccessPointKey, out var creatorRole))
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist.");
        }

        if (creatorRole < AccountRole.Administrator
            || (model.Role == AccountRole.Owner && creatorRole != AccountRole.Owner))
        {
            throw new InvalidOperationException($"The creator '{model.CreatedByKey}' doesn't have privileges to grant this access permission.");
        }

        var accessPoint = await GetAccessPointAsync(model.AccessPointKey);

        if (accessPoint is null)
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist.");
        }

        if (accessPoint.AccessMap.TryGetValue(model.AccountKey, out var accountRole)
            && accountRole >= model.Role)
        {
            return false;
        }

        var repository = _repositoryProvider.GetCore();
        var mainPartitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        accessPoint.AccessMap[model.AccountKey] = model.Role;
        await repository.Container.UpsertItemAsync(accessPoint, mainPartitionKey);
        account.AccessMap[model.AccessPointKey] = model.Role;
        await repository.Container.UpsertItemAsync(account, mainPartitionKey);
        return true;
    }

    public async Task<bool> RevokePermissionAsync(Permission model)
    {
        var creator = await GetAccountAsync(model.CreatedByKey);
        var account = await GetAccountAsync(model.AccountKey);

        if (creator is null)
        {
            throw new InvalidOperationException($"The creator '{model.CreatedByKey}' doesn't exist.");
        }

        if (account is null)
        {
            throw new InvalidOperationException($"The target account '{model.AccountKey}' doesn't exist.");
        }

        if (creator.AccessMap.TryGetValue(model.AccessPointKey, out _))
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist or the creator doesn't have permissions.");
        }

        var accessPoint = await GetAccessPointAsync(model.AccessPointKey);

        if (accessPoint is null)
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist.");
        }

        if (!accessPoint.AccessMap.TryGetValue(model.AccountKey, out var accountRole)
            || accountRole < model.Role)
        {
            return false;
        }

        if (accountRole > model.Role)
        {
            throw new InvalidOperationException($"The target account '{model.AccountKey}' has elevated role.");
        }

        var mainPartitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        var repository = _repositoryProvider.GetCore();
        accessPoint.AccessMap.Remove(model.AccountKey);
        await repository.Container.UpsertItemAsync(accessPoint, mainPartitionKey);
        account.AccessMap.Remove(model.AccessPointKey);
        await repository.Container.UpsertItemAsync(account, mainPartitionKey);
        return true;
    }

    public async Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project)
    {
        var repository = _repositoryProvider.Get(organization);
        var query = new QueryDefinition("SELECT * FROM ma");

        using var iterator = repository.Container.GetItemQueryIterator<MachineAccess>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(project),
            });

        var resultList = new List<MachineAccess>();

        while (iterator.HasMoreResults)
        {
            resultList.AddRange(await iterator.ReadNextAsync());
        }

        return resultList;
    }

    public async Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
    {
        var repository = _repositoryProvider.Get(organization);
        var response = await repository.Container.ReadItemAsync<MachineAccess>(id, new PartitionKey(project));
        return response.Resource;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var repository = _repositoryProvider.Get(model.Organization);
        var partitionKey = new PartitionKey(model.Project);

        var machineAccess = await _machineAccessService.CreateMachineAccessAsync(model.Organization);

        var accessKey = machineAccess.AccessKey;
        machineAccess = machineAccess with
        {
            AccessKey = $"{accessKey[..3]}***",
            PartitionKey = model.Project,
            Scope = model.Scope,
            AnnotationKey = model.AnnotationKey,
        };

        var response = await repository.Container.CreateItemAsync(machineAccess, partitionKey);
        machineAccess = response.Resource with { AccessKey = accessKey };
        return machineAccess;
    }

    public async Task<bool> DeleteMachineAccessAsync(string organization, string project, string id)
    {
        var repository = _repositoryProvider.Get(organization);
        var response = await repository.Container.DeleteItemAsync<MachineAccess>(id, new PartitionKey(project));

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await _machineAccessService.DeleteMachineAccessAsync(organization, id);
        }

        return response.StatusCode != HttpStatusCode.NotFound;
    }
}
