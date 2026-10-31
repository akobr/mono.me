using System.IO.Abstractions;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime;

// What the engine adapter hands to AutomationSession.StartAsync besides the settings.
public sealed class AutomationSessionOptions
{
    // The flows Target:SignIn may name; the first is the default.
    public IReadOnlyList<ISignInFlow> SignInFlows { get; set; } = [];

    public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; set; } = [];

    // Every test the engine is about to run, with its [TestCase] links.
    public IReadOnlyList<TestIdentity> PlannedTests { get; set; } = [];

    // Handed to the reporter providers; an empty provider when unset.
    public IServiceProvider? Services { get; set; }

    // The test engine and its version, e.g. "TUnit 1.76.45", for the run description.
    public string? TestEngine { get; set; }

    // Silent when unset.
    public ILogger? Logger { get; set; }

    // A FileSystem over System.IO when unset.
    public IFileSystem? FileSystem { get; set; }

    // TimeProvider.System when unset.
    public TimeProvider? TimeProvider { get; set; }

    // The GITHUB_* values and the machine of the process when unset.
    public RunEnvironment? Environment { get; set; }

    // A new yyyyMMdd-HHmmss-xxxx id when unset.
    public string? RunId { get; set; }

    // BrowserProviderFactory.CreateAsync when unset; a seam for other providers and for tests.
    public Func<BrowserProviderContext, CancellationToken, Task<IBrowserProvider>>? BrowserProviderFactory { get; set; }

    public static AutomationSessionOptions FromSetup(IUiAutomationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        return new AutomationSessionOptions
        {
            SignInFlows = setup.SignInFlows,
            ReporterProviders = setup.ReporterProviders,
            Services = setup.Services,
        };
    }
}
