using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests;
using _42.Platform.UI.Admin.AutomationTests.Access;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Reporting.Testiny;
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
// for the mock target), the reporters Reporting:Systems may select (Testiny), the checks of its own settings (Target and
// Maintenance), and the check of the target before the first test (the organization, project and view exist and a pool
// user may open them).
public sealed partial class PlatformAutomationSetup : IUiAutomationSetup
{
    public string ReadmeHint => "docs/Platform/UI.Admin.AutomationTests/automation-tests.md";

    public IReadOnlyList<ISignInFlow> SignInFlows { get; } = [new AuthKitSignIn()];

    public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; } = [TestinyReporterProvider.Instance];

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

        foreach (var (role, project) in target.RoleProjects)
        {
            CheckRoleProject(role, project, errors);
        }

        if (target.ApiTimeoutMs < 1)
        {
            errors.Add($"Target:ApiTimeoutMs {target.ApiTimeoutMs} is not positive; it is how long a wait for the API may take, e.g. 65000 (two reads at the admin UI's 30 s read timeout).");
        }

        if (target.ApiWriteTimeoutMs < 1)
        {
            errors.Add($"Target:ApiWriteTimeoutMs {target.ApiWriteTimeoutMs} is not positive; it is how long a wait for a write of the API may take, e.g. 185000 (a write at the admin UI's 120 s write timeout and two reads at its 30 s read timeout).");
        }

        // At least an hour: with 0 the purge would delete the roots of another run working in the same view right now.
        var maintenance = settings.Bind<MaintenanceSettings>(MaintenanceSettings.Section);
        if (maintenance.OlderThanHours < 1)
        {
            errors.Add($"Maintenance:OlderThanHours {maintenance.OlderThanHours} is less than 1; the purge takes the automation data of earlier runs older than this many hours, e.g. 72 (not 0: another run may be working in the view).");
        }

        if (maintenance.BudgetSeconds < 1)
        {
            errors.Add($"Maintenance:BudgetSeconds {maintenance.BudgetSeconds} is not positive; the purge stops starting requests after this many seconds, e.g. 120.");
        }

        if (settings.Users.Pool.Count == 0)
        {
            errors.Add("Users:Pool is empty; add the pre-provisioned users to testsettings.local.json and their passwords to the user secrets (docs/Platform/UI.Admin.AutomationTests/automation-tests.md, \"Configure\").");
        }
    }

    // Once per run, before the first test, as a pool user (PlatformTargetCheck); a failure fails every UI test at once.
    public Task CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PlatformTargetCheck(context.Page, context.Settings.Bind<PlatformTarget>(PlatformTarget.Section), context.User).RunAsync();
    }

    // A Target:RoleProjects entry: a role of [UserRole] (not Any) and "<org>/<project>[/<view>]" of Platform names.
    private static void CheckRoleProject(string role, string project, ICollection<string> errors)
    {
        if (!role.All(char.IsLetter) || !Enum.TryParse<Role>(role, ignoreCase: true, out var parsed) || parsed == Role.Any)
        {
            var roles = string.Join(", ", Enum.GetNames<Role>().Where(name => name != nameof(Role.Any)));
            errors.Add($"Target:RoleProjects:{role} is not a role a test acts as; use one of {roles}.");
        }

        var parts = (project ?? string.Empty).Split('/');
        if (parts.Length is not (2 or 3) || !parts.All(part => NamePattern().IsMatch(part)))
        {
            errors.Add($"Target:RoleProjects:{role} '{project}' is not <organization>/<project>[/<view>] of Platform names: 2 to 63 lower-case letters, digits and hyphens each.");
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
