using System.Threading.Tasks;
using Colorful;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class CoreDbStructureBuilder(IContainerFactory containerFactory)
{
    public async Task BuildAsync()
    {
        var orgContainerName = $"org.{Constants.ORGANIZATION}";
        var houseContainer = await containerFactory.CreateContainerIfNotExistsAsync(orgContainerName);
        await Data.CreateCoreAnnotations(houseContainer);
        Console.WriteLine($"2S platform core structure created in container '{orgContainerName}'.");

        var coreContainer = await containerFactory.CreateContainerIfNotExistsAsync("core");
        await Data.CreateCoreAccess(
            coreContainer,
            Constants.ORGANIZATION,
            Constants.BASE_ACCOUNT_ID,
            Constants.BASE_ACCOUNT_USER_NAME,
            Constants.BASE_ACCOUNT_NAME);
        Console.WriteLine("Basic access created in container 'core'.");

        // containers created before TTL and the audit indexing existed get the settings now
        var organizations = await containerFactory.EnsureOrganizationContainersAsync();
        Console.WriteLine($"Settings of {organizations.Count} organization container(s) are up to date.");
    }
}
