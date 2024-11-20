using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using AutoMapper;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Accessing;

public class CosmosAccessService : IAccessService
{
    private const string MAIN_PARTITION_KEY = "access";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IMachineAccessService _machineAccessService;
    private readonly IContainerFactory _containerFactory;
    private readonly IMapper _mapper;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosAccessService(
        IContainerRepositoryProvider repositoryProvider,
        IMachineAccessService machineAccessService,
        IContainerFactory containerFactory,
        IMapper mapper,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _machineAccessService = machineAccessService;
        _containerFactory = containerFactory;
        _mapper = mapper;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task<Account?> GetAccountAsync(string id)
    {
        var repository = _repositoryProvider.GetCore();
        var account = await repository.Container.TryReadItemAsync(
            id,
            new PartitionKey(MAIN_PARTITION_KEY),
            stream => stream.DeserializeSystemTextJson<AccountEntity>(_serializerOptions));
        return account is not null
            ? _mapper.Map<AccountEntity, Account>(account)
            : null;
    }

    public async Task<Account> CreateAccountAsync(AccountCreate model)
    {
        var name = model.Name.Trim();
        var userName = model.UserName.Trim();
        var accountId = model.IdentityId.Trim();
        var account = await GetAccountAsync(userName);

        if (account is not null)
        {
            throw new InvalidOperationException($"The account {userName}#{accountId}' already exists.");
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
        return _mapper.Map<AccountEntity, Account>(response.Resource);
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
            throw new InvalidOperationException($"The owner '{accountId}' doesn't exist.");
        }

        var accessPointIds = account.AccessMap
            .Where(pair => pair.Value >= AccountRole.Administrator)
            .Select(pair => $"apt.{pair.Key}");

        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT * FROM ap WHERE ap.Id IN @ids");
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

        return resultList.Select(entity => _mapper.Map<AccessPointEntity, AccessPoint>(entity));
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
            ? _mapper.Map<AccessPointEntity, AccessPoint>(accessPoint)
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
            throw new InvalidOperationException($"The account '{model.OwnerId}' doesn't have owner rights to the organization.");
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
            throw new InvalidOperationException($"The access point '{accessPointKey}' already exists.");
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
            await repository.Container.UpsertItemAsync(account, partitionKey);
        }

        return _mapper.Map<AccessPointEntity, AccessPoint>(response.Resource);
    }

    public async Task<bool> GrantPermissionAsync(Permission model)
    {
        if (model.Role == AccountRole.None)
        {
            throw new InvalidOperationException("The no role can't be allowed.");
        }

        var creator = await GetAccountAsync(model.CreatedById);
        var account = await GetAccountAsync(model.AccountId);

        if (creator is null)
        {
            throw new InvalidOperationException($"The creator '{model.CreatedById}' doesn't exist.");
        }

        if (account is null)
        {
            throw new InvalidOperationException($"The target account '{model.AccountId}' doesn't exist.");
        }

        if (!creator.AccessMap.TryGetValue(model.AccessPointKey, out var creatorRole))
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist.");
        }

        if (creatorRole < AccountRole.Administrator
            || model.Role == AccountRole.Owner && creatorRole != AccountRole.Owner)
        {
            throw new InvalidOperationException($"The creator '{model.CreatedById}' doesn't have privileges to grant this access permission.");
        }

        var accessPoint = await GetAccessPointAsync(model.AccessPointKey);

        if (accessPoint is null)
        {
            throw new InvalidOperationException($"The access point '{model.AccessPointKey}' doesn't exist.");
        }

        if (accessPoint.AccessMap.TryGetValue(model.AccountId, out var accountRole)
            && accountRole >= model.Role)
        {
            return false;
        }

        var repository = _repositoryProvider.GetCore();
        var mainPartitionKey = new PartitionKey(MAIN_PARTITION_KEY);

        accessPoint = accessPoint with
        {
            AccessMap = new Dictionary<string, AccountRole>(accessPoint.AccessMap)
            {
                [model.AccountId] = model.Role,
            },
        };
        await repository.Container.UpsertItemAsync(accessPoint, mainPartitionKey);

        account = account with
        {
            AccessMap = new Dictionary<string, AccountRole>(account.AccessMap)
            {
                [model.AccessPointKey] = model.Role,
            },
        };
        await repository.Container.UpsertItemAsync(account, mainPartitionKey);

        return true;
    }

    public async Task<bool> RevokePermissionAsync(Permission model)
    {
        var creator = await GetAccountAsync(model.CreatedById);
        var account = await GetAccountAsync(model.AccountId);

        if (creator is null)
        {
            throw new InvalidOperationException($"The creator '{model.CreatedById}' doesn't exist.");
        }

        if (account is null)
        {
            throw new InvalidOperationException($"The target account '{model.AccountId}' doesn't exist.");
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

        if (!accessPoint.AccessMap.TryGetValue(model.AccountId, out var accountRole)
            || accountRole < model.Role)
        {
            return false;
        }

        if (accountRole > model.Role)
        {
            throw new InvalidOperationException($"The target account '{model.AccountId}' has elevated role.");
        }

        var mainPartitionKey = new PartitionKey(MAIN_PARTITION_KEY);
        var repository = _repositoryProvider.GetCore();

        var accessPointAccessMap = new Dictionary<string, AccountRole>(accessPoint.AccessMap);
        accessPointAccessMap.Remove(model.AccountId);
        accessPoint = accessPoint with
        {
            AccessMap = accessPointAccessMap,
        };
        await repository.Container.UpsertItemAsync(accessPoint, mainPartitionKey);

        var accountAccessMap = new Dictionary<string, AccountRole>(account.AccessMap);
        accountAccessMap.Remove(model.AccessPointKey);
        account = account with
        {
            AccessMap = accountAccessMap,
        };
        await repository.Container.UpsertItemAsync(account, mainPartitionKey);

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

        return resultList.Select(entity => _mapper.Map<MachineAccessEntity, MachineAccess>(entity));
    }

    public async Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var machineAccess = await repository.Container.TryReadItemAsync(
            id,
            new PartitionKey(project),
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));
        return machineAccess is not null
            ? _mapper.Map<MachineAccessEntity, MachineAccess>(machineAccess)
            : null;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(model.Organization);
        var partitionKey = new PartitionKey($"{model.Project}.access");

        var machineAccess = await _machineAccessService.CreateMachineAccessAsync(model);

        var accessKey = machineAccess.AccessKey;
        machineAccess = machineAccess with
        {
            AccessKey = $"{accessKey[..3]}***",
        };

        var response = await repository.Container.CreateItemAsync(machineAccess, partitionKey);
        machineAccess = response.Resource with { AccessKey = accessKey };
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
            throw new InvalidOperationException($"The machine access {appId} has not been found.");
        }

        var accessKey = await _machineAccessService.ResetMachineAccessAsync(machineAccess.Id);

        if (accessKey is null)
        {
            throw new InvalidOperationException($"The machine access {appId} reset failed.");
        }

        machineAccess = machineAccess with { AccessKey = $"{accessKey[..3]}***" };
        await repository.Container.UpsertItemAsync(machineAccess, partitionKey);
        machineAccess = machineAccess with { AccessKey = accessKey };
        return _mapper.Map<MachineAccessEntity, MachineAccess>(machineAccess);
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
            return false;
        }

        var response = await repository.Container.DeleteItemAsync<MachineAccessEntity>(appId, partitionKey);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await _machineAccessService.DeleteMachineAccessAsync(machineAccess.ObjectId);
        }

        return response.StatusCode != HttpStatusCode.NotFound;
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
}
