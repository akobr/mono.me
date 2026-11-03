using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Platform's keys of the Target section, next to the library's BaseUrl and SignIn: the pre-provisioned
// organization, project and view the tests work in, the dataset of a mock target, the domain of generated e-mail
// addresses, the projects of the exact roles and how long a wait for the API may take. Read with
// Settings.Bind<PlatformTarget>("Target"); the defaults are in testsettings.json.
public sealed class PlatformTarget : TargetSettings
{
    public const string Section = "Target";

    // ApiTimeoutMs of testsettings.json, also App's default: two reads at the admin UI's 30 s read timeout, plus a
    // margin.
    public const int DefaultApiTimeoutMs = 65_000;

    // ApiWriteTimeoutMs of testsettings.json, also App's default: a write at the admin UI's 120 s write timeout, then
    // the two reads at its 30 s read timeout with which a configuration save that timed out reads its content again,
    // plus a margin.
    public const int DefaultApiWriteTimeoutMs = 185_000;

    private Dictionary<string, string> roleProjects = new(StringComparer.OrdinalIgnoreCase);

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string View { get; set; } = string.Empty;

    // ?mock-scenario=<name> on the first navigation of each test, e.g. default (also new-user, invited, large and
    // showcase). Only the admin UI's mock build reads it; a deployed build ignores it.
    public string? MockScenario { get; set; }

    // Generated addresses are sub-addresses on it: ui-tests+<slug>@<EmailDomain>.
    public string EmailDomain { get; set; } = "example.com";

    // How long a wait for the answer to a read may take (App.ApiTimeoutMs): two reads in a row (an annotation, then its
    // history or its deletion) at the admin UI's read timeout of 30 s, plus a margin, so that a stalled request ends in
    // the UI's error state, which the wait reports, rather than in a bare test timeout. A wait that passes is not
    // slower; Execution:ExpectTimeoutMs stays for everything else.
    public int ApiTimeoutMs { get; set; } = DefaultApiTimeoutMs;

    // How long a wait for the answer to a write may take (App.ApiWriteTimeoutMs): a save's toast, a created
    // configuration (Bulk create, two writes in a row, waits twice as long). The admin UI gives a write 120 s, and a
    // configuration save that timed out reads its content again before its toast, so ApiTimeoutMs would end such a wait
    // before the UI's error.
    public int ApiWriteTimeoutMs { get; set; } = DefaultApiWriteTimeoutMs;

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
