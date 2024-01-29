using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class Data
{
    public static async Task CreateCore(Container container)
    {
        // responsibilities
        await container.UpsertItemAsync(Create.Responsibility("storyteller"));
        await container.UpsertItemAsync(Create.Responsibility("supervisor"));
        await container.UpsertItemAsync(Create.Responsibility("scheduler"));

        // subjects
        await container.UpsertItemAsync(Create.Subject(
            "42",
            new[] { "storyteller", "supervisor", "scheduler" },
            new[] { "system" }));

        // contexts
        await container.UpsertItemAsync(Create.Context("42", "system"));

        // usages
        await container.UpsertItemAsync(Create.Usage("42", "storyteller", new[] { "system" }));
        await container.UpsertItemAsync(Create.Usage("42", "supervisor", new[] { "system" }));
        await container.UpsertItemAsync(Create.Usage("42", "scheduler", new[] { "system" }));

        // executions
        await container.UpsertItemAsync(Create.Execution("42", "storyteller", "system"));
        await container.UpsertItemAsync(Create.Execution("42", "supervisor", "system"));
        await container.UpsertItemAsync(Create.Execution("42", "scheduler", "system"));
    }
}
