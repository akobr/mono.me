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

    // Checks the project's own settings once they are loaded and Target:BaseUrl is set, e.g. through
    // settings.Bind<PlatformTarget>("Target"); each message added to errors fails the load.
    void Validate(UiAutomationSettings settings, ICollection<string> errors)
    {
    }
}
