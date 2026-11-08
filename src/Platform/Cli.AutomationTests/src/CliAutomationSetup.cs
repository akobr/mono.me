using _42.Platform.Cli.AutomationTests;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Reporting.Testiny;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.TUnit;

// The library's session hooks find the setup here; the receivers report every result and retry per Execution:Retries.
// No BrowserStack limit: the CLI's runs use a browser only for the device page, in the local headless Chromium.
[assembly: UiAutomationSetup<CliAutomationSetup>]
[assembly: ReportResults]
[assembly: ConfiguredRetry]

namespace _42.Platform.Cli.AutomationTests;

// What the CLI suite adds to the UI automation runtime: no sign-in flow of the runtime (Target:SignIn None: a pool user
// signs in through sform's device code, whose AuthKit page asks for the credentials), the reporters Reporting:Systems may
// select (Testiny), the checks of its own settings (Target, Cli, Machines, the pool), and the check of the target before
// the first test without a page (CliTargetCheck: a pool user signs in through sform and reads the organization, project
// and view; each configured machine signs in).
public sealed class CliAutomationSetup : IUiAutomationSetup
{
    public string ReadmeHint => "docs/Platform/Cli.AutomationTests/automation-tests.md";

    public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; } = [TestinyReporterProvider.Instance];

    // The check signs its user in through sform and opens a browser only to approve the device code.
    public bool TargetCheckUsesBrowser => false;

    public void Validate(UiAutomationSettings settings, ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(errors);

        // The Platform's shared keys first (organization, project, view, Target:RoleProjects), then the CLI's.
        var target = settings.Bind<CliTarget>(PlatformTargetSettings.Section);
        target.ValidateCli(errors);

        if (!string.IsNullOrWhiteSpace(target.SignIn) && !string.Equals(target.SignIn, TargetSettings.NoSignIn, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Target:SignIn '{target.SignIn}' is not None: the CLI's pool users sign in through sform's device code, whose AuthKit page asks for the credentials; the suite registers no sign-in flow of the runtime.");
        }

        var cli = settings.Bind<CliSettings>(CliSettings.Section);
        if (cli.TimeoutMs < 1_000)
        {
            errors.Add($"Cli:TimeoutMs {cli.TimeoutMs} is less than 1000; it is how long one run of sform may take, e.g. 120000.");
        }

        if (SformLocator.Check(cli.Path) is { } missing)
        {
            errors.Add(missing);
        }

        foreach (var (name, machine) in settings.Bind<MachineCatalog>(MachineCatalog.Section))
        {
            ValidateMachine(name, machine, errors);
        }

        if (settings.Users.Pool.Count == 0)
        {
            errors.Add("Users:Pool is empty; the CLI's pool users are in testsettings.json and their passwords in the user secrets (docs/Platform/Cli.AutomationTests/automation-tests.md, \"Users\").");
        }

        for (var index = 0; index < settings.Users.Pool.Count; index++)
        {
            if (string.IsNullOrEmpty(settings.Users.Pool[index].Password))
            {
                errors.Add($"Users:Pool:{index}:Password is required: the pool user types it on the AuthKit page of sform's device code; set it in the user secrets or UITESTS_Users__Pool__{index}__Password.");
            }
        }
    }

    // Once per run, before the first test, as a pool user (CliTargetCheck); a failure fails every CLI test at once.
    public Task CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CliTargetCheck(context, cancellationToken).RunAsync();
    }

    private static void ValidateMachine(string name, MachineSettings machine, ICollection<string> errors)
    {
        var kinds = new[]
        {
            !string.IsNullOrWhiteSpace(machine.ApiKey) || !string.IsNullOrWhiteSpace(machine.Certificate),
            !string.IsNullOrWhiteSpace(machine.ClientId) || !string.IsNullOrWhiteSpace(machine.ClientSecret),
        }.Count(isSet => isSet);

        if (kinds == 0)
        {
            errors.Add($"Machines:{name} has no credential: set ApiKey, Certificate (with CertificatePassword), or ClientId and ClientSecret; the secrets in the user secrets or UITESTS_Machines__{name}__<Key>.");
        }
        else if (kinds > 1)
        {
            errors.Add($"Machines:{name} sets the variables of more than one credential kind (an API key or certificate, and client credentials); sform refuses that.");
        }

        if (!string.IsNullOrWhiteSpace(machine.ClientId) != !string.IsNullOrWhiteSpace(machine.ClientSecret))
        {
            errors.Add($"Machines:{name} needs both ClientId and ClientSecret for client credentials.");
        }

        if (machine.Project is { } project && (project.Split('.') is not [{ } organization, { } projectName] || !PlatformNames.IsValid(organization) || !PlatformNames.IsValid(projectName)))
        {
            errors.Add($"Machines:{name}:Project '{project}' is not <organization>.<project> of Platform names.");
        }

        if (machine.Endpoint is { } endpoint && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            errors.Add($"Machines:{name}:Endpoint '{endpoint}' is not an absolute URL; leave it null for Target:BaseUrl.");
        }
    }
}
