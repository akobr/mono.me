using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

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
}
