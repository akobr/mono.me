using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.Logic;

// Accounts of the AuthKit staging environment the seed grants access to. The ids are the users' AuthKit subs; their
// passwords live in AuthKit and in the settings of the UI automation tests, never here.
public static class SeedAccounts
{
    public const string UI_TESTS_ORGANIZATION = "ui-tests";
    public const string UI_TESTS_PROJECT = "automation";

    // The default user of AuthKit staging, owner of the base organization like the configured base account.
    public static readonly SeedAccount AuthKitOwner = new(
        "user_01M41P6G2TTSXXMM88WNVMRQKG",
        Constants.BASE_ACCOUNT_USER_NAME,
        "Ales Kobr",
        AccountRole.Owner);

    // The users of the UI automation tests, members of ui-tests and ui-tests.automation.
    public static readonly IReadOnlyList<SeedAccount> UiTestUsers =
    [
        new("user_01M6CQD2N4BDN7ATA6M3BFMH14", "ui-test-owner-1@42for.software", "Auto Owner1", AccountRole.Owner),
        new("user_01M6CSHVS51E5PNJEZDHAD67EJ", "ui-test-owner-2@42for.software", "Auto Owner2", AccountRole.Owner),
        new("user_01M6CSKGDCGAWZK8DZ6Y8CVY3A", "ui-test-admin-1@42for.software", "Auto Admin1", AccountRole.Administrator),
        new("user_01M6CSSS2TMRN24GPHYZR5D6M0", "ui-test-admin-2@42for.software", "Auto Admin2", AccountRole.Administrator),
        new("user_01M6CSVQRGSW27YKH039HE64F8", "ui-test-contributor-1@42for.software", "Auto Contributor1", AccountRole.Contributor),
        new("user_01M6CSXPB2H9QTH91M69RTJ7Y0", "ui-test-contributor-2@42for.software", "Auto Contributor2", AccountRole.Contributor),
        new("user_01M6CSZ9JC54XWR7NBXBF81Y7V", "ui-test-reader-1@42for.software", "Auto Reader1", AccountRole.Reader),
        new("user_01M6CT0VP7F0X5689B06VZ4A60", "ui-test-reader-2@42for.software", "Auto Reader2", AccountRole.Reader),
    ];

    public static IReadOnlyDictionary<string, AccountRole> Roles(AccountRole role, params string[] organizations)
    {
        var roles = new Dictionary<string, AccountRole>();
        foreach (var organization in organizations)
        {
            roles[organization] = role;
        }

        return roles;
    }
}

public sealed record SeedAccount(string Id, string UserName, string Name, AccountRole Role);
