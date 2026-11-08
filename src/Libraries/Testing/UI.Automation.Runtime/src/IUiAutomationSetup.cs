using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime;

// What a consuming test project contributes to the engine-neutral runtime; one type in the test assembly, which the
// engine adapter finds there (e.g. [assembly: UiAutomationSetup<PlatformAutomationSetup>] with TUnit) and creates
// with its parameterless constructor.
public interface IUiAutomationSetup
{
    // Where the project documents its settings; ends the skip reason "UITESTS_Target__BaseUrl is not configured; see …".
    string ReadmeHint { get; }

    // The flows Target:SignIn may name; the first is the default.
    IReadOnlyList<ISignInFlow> SignInFlows => [];

    // The reporters Reporting:Systems may select.
    IReadOnlyList<ITestRunReporterProvider> ReporterProviders => [];

    // Handed to the reporter providers; an empty provider when null.
    IServiceProvider? Services => null;

    // Whether CheckTargetAsync gets a signed-in page up front (the default). False: the check's scope leases the pool
    // user but opens no browser, context.Page and context.Context throw, and the check opens a browser context when it
    // needs one (context.OpenBrowserAsync), e.g. a check through a command line that needs a browser only to approve
    // the device code of its sign-in. With BrowserProviderStart.OnFirstLease such a check starts no browser provider
    // before it asks for a browser.
    bool TargetCheckUsesBrowser => true;

    // Checks the project's own settings once they are loaded and Target:BaseUrl is set, e.g. through
    // settings.Bind<PlatformTarget>("Target"); each message added to errors fails the load.
    void Validate(UiAutomationSettings settings, ICollection<string> errors)
    {
    }

    // Checks once per run that the target can run the tests (e.g. the pre-provisioned organization and project exist
    // and the user may open them), on the page of context, signed in as a leased pool user (or with the user only, see
    // TargetCheckUsesBrowser); throws when it cannot, best a TargetCheckException whose message says what to fix. The
    // session runs it at the end of its start, before any reporter creates a run: a failure fails the start, so every
    // UI test fails at once with the check's message. Only a setup that implements it gets a check (and its browser
    // context); none by default.
    Task CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
