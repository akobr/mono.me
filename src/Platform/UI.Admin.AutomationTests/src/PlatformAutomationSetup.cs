using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests;
using _42.Platform.UI.Admin.AutomationTests.Access;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.TUnit;

// The library's session hooks find the setup here; the receivers report every result, retry per Execution:Retries and
// limit the tests to the BrowserStack sessions when that provider runs them.
[assembly: UiAutomationSetup<PlatformAutomationSetup>]
[assembly: ReportResults]
[assembly: ConfiguredRetry]
[assembly: BrowserStackSessionLimit]

namespace _42.Platform.UI.Admin.AutomationTests;

// What the Platform adds to the UI automation runtime: the AuthKit sign-in (Target:SignIn AuthKit, the default; None
// for the mock target), the reporters (Testiny comes with the reporting phase) and the checks of its own settings.
public sealed partial class PlatformAutomationSetup : IUiAutomationSetup
{
    public string ReadmeHint => "src/Platform/UI.Admin.AutomationTests/README.md";

    public IReadOnlyList<ISignInFlow> SignInFlows { get; } = [new AuthKitSignIn()];

    public IReadOnlyList<ITestRunReporterProvider> ReporterProviders => [];

    public void Validate(UiAutomationSettings settings, ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(errors);

        var target = settings.Bind<PlatformTarget>(PlatformTarget.Section);
        RequireName(target.Organization, "Target:Organization", errors);
        RequireName(target.Project, "Target:Project", errors);
        RequireName(target.View, "Target:View", errors);

        if (target.MockScenario is { } scenario && !ScenarioPattern().IsMatch(scenario))
        {
            errors.Add($"Target:MockScenario '{scenario}' is not a scenario name of the mock build (e.g. default).");
        }

        if (string.IsNullOrWhiteSpace(target.EmailDomain) || target.EmailDomain.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("Target:EmailDomain must be a domain such as example.com.");
        }

        if (settings.Users.Pool.Count == 0)
        {
            errors.Add("Users:Pool is empty; add the pre-provisioned users to testsettings.local.json and their passwords to the user secrets (README.md, \"Configure\").");
        }
    }

    private static void RequireName(string value, string setting, ICollection<string> errors)
    {
        if (!NamePattern().IsMatch(value))
        {
            errors.Add($"{setting} '{value}' is not a Platform name: 2 to 63 lower-case letters, digits and hyphens, not starting with a hyphen.");
        }
    }

    // NameRules.Pattern of Backend.Core.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ScenarioPattern();
}
