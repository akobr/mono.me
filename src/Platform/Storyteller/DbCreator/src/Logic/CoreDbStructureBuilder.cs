using System.Threading.Tasks;
using _42.Platform.Storyteller.DbCreator.Configuration;
using Colorful;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class CoreDbStructureBuilder(IContainerFactory containerFactory, IOptions<BaseAccountOptions> baseAccount)
{
    public async Task BuildAsync()
    {
        var orgContainerName = $"org.{Constants.ORGANIZATION}";
        var houseContainer = await containerFactory.CreateContainerIfNotExistsAsync(orgContainerName);
        await Data.CreateCoreAnnotations(houseContainer);
        Console.WriteLine($"2S platform core structure created in container '{orgContainerName}'.");

        var uiTestsContainerName = $"org.{SeedAccounts.UI_TESTS_ORGANIZATION}";
        await containerFactory.CreateContainerIfNotExistsAsync(uiTestsContainerName);
        Console.WriteLine($"Organization container '{uiTestsContainerName}' created.");

        // Access is merged into what is stored, so members and memberships added later survive another run.
        var coreContainer = await containerFactory.CreateContainerIfNotExistsAsync("core");
        var baseProject = $"{Constants.ORGANIZATION}.{Storyteller.Constants.DefaultProjectName}";
        var uiTestsProject = $"{SeedAccounts.UI_TESTS_ORGANIZATION}.{SeedAccounts.UI_TESTS_PROJECT}";
        var owners = new[]
        {
            new SeedAccount(baseAccount.Value.GetAccountId(), Constants.BASE_ACCOUNT_USER_NAME, Constants.BASE_ACCOUNT_NAME, AccountRole.Owner),
            SeedAccounts.AuthKitOwner,
        };

        foreach (var owner in owners)
        {
            await SeedAsync(coreContainer, owner, Constants.ORGANIZATION, baseProject, SeedAccounts.UI_TESTS_ORGANIZATION, uiTestsProject);
        }

        foreach (var user in SeedAccounts.UiTestUsers)
        {
            await SeedAsync(coreContainer, user, SeedAccounts.UI_TESTS_ORGANIZATION, uiTestsProject);
        }

        Console.WriteLine($"Access created in container 'core': {owners.Length} owners, {SeedAccounts.UiTestUsers.Count} UI test users.");

        // containers created before TTL and the audit indexing existed get the settings now
        var organizations = await containerFactory.EnsureOrganizationContainersAsync();
        Console.WriteLine($"Settings of {organizations.Count} organization container(s) are up to date.");
    }

    private static Task SeedAsync(Container container, SeedAccount account, params string[] accessPoints)
    {
        return Data.SeedAccountAccessAsync(
            container,
            account.Id,
            account.UserName,
            account.Name,
            SeedAccounts.Roles(account.Role, accessPoints));
    }
}
