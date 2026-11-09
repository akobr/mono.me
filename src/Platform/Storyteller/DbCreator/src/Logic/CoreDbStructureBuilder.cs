using System.Threading.Tasks;
using _42.Platform.Storyteller.DbCreator.Configuration;
using Colorful;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class CoreDbStructureBuilder(
    IContainerFactory containerFactory,
    IOptions<BaseAccountOptions> baseAccount,
    IOptions<StagingSeedOptions> stagingSeed)
{
    public async Task BuildAsync()
    {
        // Checked before anything is written: the data of the layout before the shared container is not migrated, the
        // database is seeded again.
        var problems = await containerFactory.FindLayoutProblemsAsync();
        if (problems.Count > 0)
        {
            throw new DatabaseLayoutException(problems);
        }

        var coreContainer = await containerFactory.CreateContainerIfNotExistsAsync(CosmosConstants.CoreContainerName);

        // created with its settings, or an existing one gets the settings it misses (TTL, the audit indexing)
        var organizationsContainer = await containerFactory.EnsureOrganizationsContainerAsync();
        Console.WriteLine($"Containers '{CosmosConstants.CoreContainerName}' and '{CosmosConstants.OrganizationsContainerName}' are ready, the settings of '{CosmosConstants.OrganizationsContainerName}' are up to date.");

        await Data.CreateCoreAnnotations(new OrganizationContainer(Constants.ORGANIZATION, organizationsContainer));
        Console.WriteLine($"2S platform core structure of '{Constants.ORGANIZATION}' created in container '{CosmosConstants.OrganizationsContainerName}'.");

        // Access is merged into what is stored, so members and memberships added later survive another run.
        var baseProject = $"{Constants.ORGANIZATION}.{Storyteller.Constants.DefaultProjectName}";
        var baseOwner = new SeedAccount(baseAccount.Value.GetAccountId(), Constants.BASE_ACCOUNT_USER_NAME, Constants.BASE_ACCOUNT_NAME, AccountRole.Owner);

        if (!stagingSeed.Value.Enabled)
        {
            await SeedAsync(coreContainer, baseOwner, Constants.ORGANIZATION, baseProject);
            Console.WriteLine($"Access created in container '{CosmosConstants.CoreContainerName}'. The AuthKit staging accounts are skipped ({StagingSeedOptions.SECTION}:enabled is not true).");
        }
        else
        {
            // An organization has no container of its own: its access point in core is all the UI test organization needs.
            var uiTestsProject = $"{SeedAccounts.UI_TESTS_ORGANIZATION}.{SeedAccounts.UI_TESTS_PROJECT}";
            var owners = new[] { baseOwner, SeedAccounts.AuthKitOwner };
            foreach (var owner in owners)
            {
                await SeedAsync(coreContainer, owner, Constants.ORGANIZATION, baseProject, SeedAccounts.UI_TESTS_ORGANIZATION, uiTestsProject);
            }

            foreach (var user in SeedAccounts.UiTestUsers)
            {
                await SeedAsync(coreContainer, user, SeedAccounts.UI_TESTS_ORGANIZATION, uiTestsProject);
            }

            Console.WriteLine($"Access created in container '{CosmosConstants.CoreContainerName}': {owners.Length} owners, {SeedAccounts.UiTestUsers.Count} UI test users.");
        }
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
