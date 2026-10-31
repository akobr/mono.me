using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Platform's keys of the Target section, next to the library's BaseUrl and SignIn: the pre-provisioned
// organization, project and view the tests work in, the dataset of a mock target and the domain of generated e-mail
// addresses. Read with Settings.Bind<PlatformTarget>("Target"); the defaults are in testsettings.json.
public sealed class PlatformTarget : TargetSettings
{
    public const string Section = "Target";

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string View { get; set; } = string.Empty;

    // ?mock-scenario=<name> on the first navigation of each test, e.g. default (also new-user, invited, large and
    // showcase). Only the admin UI's mock build reads it; a deployed build ignores it.
    public string? MockScenario { get; set; }

    // Generated addresses are sub-addresses on it: ui-tests+<slug>@<EmailDomain>.
    public string EmailDomain { get; set; } = "example.com";
}
