using System.Threading.Tasks;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using Colorful;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class CoreDbStructureBuilder(
    IContainerFactory containerFactory,
    DataSetSeeder seeder,
    IOptions<BaseAccountOptions> baseAccount,
    IOptions<StagingSeedOptions> stagingSeed)
{
    // The built-in set of the platform's own annotations, seeded into 42dotnet / main / default by every run.
    public const string CORE_SET = "core";

    // The layout check, the containers, the access of the base owner (and the staging accounts), then the set core.
    // The seed command calls EnsureContainersAsync alone.
    public async Task<DataSetRunResult> BuildAsync()
    {
        var coreContainer = await EnsureContainersAsync();

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

        // After the access, so the project exists and is used as it is. Its target is in its set.json; the command
        // prints the result.
        return await seeder.SeedAsync(new DataSetSeedRequest { Sets = [CORE_SET] });
    }

    // Checked before anything is written: the data of the layout before the shared container is not migrated, the
    // database is seeded again. Then the containers, the organizations one with its settings; returns core.
    public async Task<Container> EnsureContainersAsync()
    {
        var problems = await containerFactory.FindLayoutProblemsAsync();
        if (problems.Count > 0)
        {
            throw new DatabaseLayoutException(problems);
        }

        var coreContainer = await containerFactory.CreateContainerIfNotExistsAsync(CosmosConstants.CoreContainerName);

        // created with its settings, or an existing one gets the settings it misses (TTL, the audit indexing)
        await containerFactory.EnsureOrganizationsContainerAsync();
        Console.WriteLine($"Containers '{CosmosConstants.CoreContainerName}' and '{CosmosConstants.OrganizationsContainerName}' are ready, the settings of '{CosmosConstants.OrganizationsContainerName}' are up to date.");

        return coreContainer;
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
