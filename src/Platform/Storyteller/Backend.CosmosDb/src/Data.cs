using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public class Data
{
    public static async Task CreateCoreAnnotations(Container container)
    {
        // responsibilities
        await container.UpsertItemAsync(Create.Responsibility("storyteller"));
        await container.UpsertItemAsync(Create.Responsibility("supervisor"));
        await container.UpsertItemAsync(Create.Responsibility("scheduler"));

        // subjects
        await container.UpsertItemAsync(Create.Subject(
            "42",
            ["storyteller", "supervisor", "scheduler"],
            ["system"]));

        // contexts
        await container.UpsertItemAsync(Create.Context(
            "42",
            "system",
            ["storyteller", "supervisor", "scheduler"]));

        // usages
        await container.UpsertItemAsync(Create.Usage("42", "storyteller", ["system"]));
        await container.UpsertItemAsync(Create.Usage("42", "supervisor", ["system"]));
        await container.UpsertItemAsync(Create.Usage("42", "scheduler", ["system"]));

        // executions
        await container.UpsertItemAsync(Create.Execution("42", "storyteller", "system"));
        await container.UpsertItemAsync(Create.Execution("42", "supervisor", "system"));
        await container.UpsertItemAsync(Create.Execution("42", "scheduler", "system"));

        // configurations
        await container.UpsertItemAsync(Create.Configuration(
            AnnotationKey.CreateContext("42", "system"),
            JObject.Parse("""{ "IsSystem": true, "Number": 42, "Text": "system" }""")));
    }

    public static async Task CreateCoreAccess(
        Container container,
        string organizationName,
        string accountId,
        string accountUserName,
        string accountName)
    {
        await container.UpsertItemAsync(
            Create.BaseAccount(
                organizationName,
                accountId,
                accountUserName,
                accountName));

        var accessPoints = Create.BaseAccessPoints(organizationName, accountId);
        foreach (var accessPoint in accessPoints)
        {
            await container.UpsertItemAsync(accessPoint);
        }
    }

    // Grants the account the given roles and adds it to each access point with the same role, keeping what is already
    // stored: other memberships of the account, other members and settings of the access points. Missing access points
    // are created; a new organization needs its container (org.{name}) created by the caller.
    public static async Task SeedAccountAccessAsync(
        Container container,
        string accountId,
        string accountUserName,
        string accountName,
        IReadOnlyDictionary<string, AccountRole> roles)
    {
        var partitionKey = new PartitionKey("access");
        var account = await TryReadAsync<AccountEntity>(container, accountId, partitionKey);
        var accountMap = new Dictionary<string, AccountRole>(account?.AccessMap ?? []);
        foreach (var (key, role) in roles)
        {
            accountMap[key] = role;
        }

        await container.UpsertItemAsync(
            new AccountEntity { Id = accountId, UserName = accountUserName, Name = accountName, AccessMap = accountMap },
            partitionKey);

        foreach (var (key, role) in roles)
        {
            var accessPoint = await TryReadAsync<AccessPointEntity>(container, $"apt.{key}", partitionKey);
            var accessMap = new Dictionary<string, AccountRole>(accessPoint?.AccessMap ?? []) { [accountId] = role };
            var updated = accessPoint is null
                ? new AccessPointEntity { Key = key, AccessMap = accessMap }
                : accessPoint with { AccessMap = accessMap };
            await container.UpsertItemAsync(updated, partitionKey);
        }
    }

    private static async Task<T?> TryReadAsync<T>(Container container, string id, PartitionKey partitionKey)
        where T : class
    {
        try
        {
            return (await container.ReadItemAsync<T>(id, partitionKey)).Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
