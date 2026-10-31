using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.UI.Admin.AutomationTests;

// The base class of the admin UI's tests: the Platform's target (organization, project, view), its generated data and
// the app shell, whose first navigation carries the mock scenario of a mock target. TUnit creates an instance per
// attempt, so the page objects belong to one attempt.
public abstract class PlatformTest : UiTest
{
    private App? app;
    private PlatformData? platformData;

    protected PlatformTarget Target => Settings.Bind<PlatformTarget>(PlatformTarget.Section);

    protected App App => app ??= new App(Page, Target.MockScenario);

    protected PlatformData PlatformData => platformData ??= new PlatformData(Data, Session.RunId, Target.EmailDomain);
}
