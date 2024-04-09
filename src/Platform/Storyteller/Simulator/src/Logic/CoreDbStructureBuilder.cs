using System.Threading.Tasks;
using Colorful;

namespace _42.Platform.Storyteller.Simulator.Logic;

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
    }
}
