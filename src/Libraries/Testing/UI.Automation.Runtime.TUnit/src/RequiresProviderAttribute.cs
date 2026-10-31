using _42.Testing.UI.Automation.Runtime.Settings;
using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Skips the test unless Browser:Provider is the given one, e.g. a check of a remote session's status. Decided at
// registration from the settings loaded before discovery; an attribute skip runs no hooks, so the end receiver reports
// it. Without a target the test is skipped like every UI test.
public sealed class RequiresProviderAttribute(BrowserProviderKind provider) : SkipAttribute($"Requires Browser:Provider {provider}.")
{
    public BrowserProviderKind Provider { get; } = provider;

    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(UiAutomationSession.LoadedSettings switch
        {
            null => false,
            { IsConfigured: false } or { IsReplay: true } => true,
            var settings => settings.Browser.Provider != Provider,
        });
    }

    protected override string GetSkipReason(TestRegisteredContext context)
    {
        return UiAutomationSession.SkipReason
            ?? $"Requires Browser:Provider {Provider}; the run uses {UiAutomationSession.LoadedSettings?.Browser.Provider}.";
    }
}
