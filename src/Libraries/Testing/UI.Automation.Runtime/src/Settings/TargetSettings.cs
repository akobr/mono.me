namespace _42.Testing.UI.Automation.Runtime.Settings;

// The Target section: what the suite runs against. A consuming project adds its own keys to the same section and reads
// them with UiAutomationSettings.Bind<T>("Target"), e.g. the organization, project and view of the Platform; a type
// that derives from this one also gets BaseUrl and SignIn.
public class TargetSettings
{
    // The value of SignIn for a target that needs no sign-in: a mock build that is signed in already, or a public site.
    public const string NoSignIn = "None";

    // The absolute http or https URL of the application, the BaseURL of every browser context. Empty puts the suite in
    // skip mode: nothing else is validated and no browser starts.
    public string? BaseUrl { get; set; }

    // Which sign-in flow signs the pool users in: the Name of one of the flows the setup registers (e.g. AuthKit), None,
    // or unset for the first registered flow (None when there is none). The runtime owns the mechanics (one sign-in per
    // user per session, cached storage state, passwords required only when the flow needs them); what a flow does on
    // the page belongs to the consuming project.
    public string? SignIn { get; set; }
}
