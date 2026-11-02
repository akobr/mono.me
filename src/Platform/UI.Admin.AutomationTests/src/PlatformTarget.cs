using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Platform's keys of the Target section, next to the library's BaseUrl and SignIn: the pre-provisioned
// organization, project and view the tests work in, the dataset of a mock target, the domain of generated e-mail
// addresses and the projects of the exact roles. Read with Settings.Bind<PlatformTarget>("Target"); the defaults are in
// testsettings.json.
public sealed class PlatformTarget : TargetSettings
{
    public const string Section = "Target";

    private Dictionary<string, string> roleProjects = new(StringComparer.OrdinalIgnoreCase);

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string View { get; set; } = string.Empty;

    // ?mock-scenario=<name> on the first navigation of each test, e.g. default (also new-user, invited, large and
    // showcase). Only the admin UI's mock build reads it; a deployed build ignores it.
    public string? MockScenario { get; set; }

    // Generated addresses are sub-addresses on it: ui-tests+<slug>@<EmailDomain>.
    public string EmailDomain { get; set; } = "example.com";

    // By a Role name (Contributor, Reader, …; case-insensitive), "<org>/<project>[/<view>]": a project in which every
    // pool user holds that role, so a test of the role ([UserRole]) works there with any pool user. A role without an
    // entry leases a pool user who holds it in Target:Project. Empty by default; the mock maps Contributor and Reader to
    // the projects where Ada Lovelace holds them. The target check opens /orgs to verify each entry.
    public Dictionary<string, string> RoleProjects
    {
        get => roleProjects;
        set => roleProjects = new Dictionary<string, string>(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    // The admin UI's mock build (MSW serves the API in the browser and Ada Lovelace is signed in): a mock scenario is
    // set, or the target needs no sign-in. [MockOnly] tests run only here, and PlatformTest.MockApi works only here.
    public bool IsMock => !string.IsNullOrWhiteSpace(MockScenario) || string.Equals(SignIn, NoSignIn, StringComparison.OrdinalIgnoreCase);
}
