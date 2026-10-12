#nullable enable

using System;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

internal static class KeyValueTestHelpers
{
    // The policy lives on the project's access point; tests of the policy create one first.
    public static async Task<string> CreateProjectWithAccessPointAsync(this ITestContext context, string name)
    {
        var project = $"kv-{name}-{Guid.NewGuid().ToString("N")[..12]}";
        var core = context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        await core.CreateItemAsync(
            new AccessPointEntity { Key = $"{TestConstants.Organization}.{project}", AccessMap = new() },
            new PartitionKey("access"));
        return project;
    }
}
