using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// What a browser provider starts with: the settings, the values of the run's placeholders (BrowserStack:Build uses
// them), every test the engine is about to run (a BrowserStack session per class closes after the last test of its
// class), the session's logger and clock.
public sealed record BrowserProviderContext(UiAutomationSettings Settings, RunNameValues Run)
{
    public IReadOnlyList<TestIdentity> PlannedTests { get; init; } = [];

    public ILogger Logger { get; init; } = NullLogger.Instance;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    // A run of the settings that starts now, in the GITHUB_* environment and on the machine of the process, with no
    // planned tests.
    public static BrowserProviderContext ForSettings(UiAutomationSettings settings, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = DateTimeOffset.Now;
        return new BrowserProviderContext(settings, RunNameValues.For(settings, AutomationSession.NewRunId(now), now, RunEnvironment.Current))
        {
            Logger = logger ?? NullLogger.Instance,
        };
    }
}
