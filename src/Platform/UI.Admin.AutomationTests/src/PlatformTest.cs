using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.UI.Admin.AutomationTests;

// The base class of the admin UI's tests: the Platform's target (organization, project, view) and its routes, the
// generated data and the app shell, whose first navigation carries the mock scenario of a mock target. TUnit creates
// an instance per attempt, so the page objects belong to one attempt. The role a test acts as, its mode skips and its
// routes are in PlatformTest.Roles.cs; the helpers (a disposable project, the clipboard, requests, downloads, the mock
// API, a second page) in PlatformTest.Helpers.cs.
public abstract partial class PlatformTest : UiTest
{
    private App? app;
    private PlatformData? platformData;

    // A pool user who may create and edit in the target's project, Contributor and up: a Reader gets no create, edit or
    // save action. [UserRole(Role.Editor)] asks for it; the Roles of Users:Pool are the users' roles in the target's
    // organization and project.
    protected static UserRequirement EditingUser => PlatformRoles.Editing;

    protected PlatformTarget Target => Settings.Bind<PlatformTarget>(PlatformTarget.Section);

    protected App App => app ??= new App(Page, Target.MockScenario, Target.ApiTimeoutMs, Target.ApiWriteTimeoutMs);

    protected PlatformData PlatformData => platformData ??= new PlatformData(Data, Session.RunId, Target.EmailDomain);
}
