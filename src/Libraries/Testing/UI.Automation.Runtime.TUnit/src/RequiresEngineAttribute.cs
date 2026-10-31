using _42.Testing.UI.Automation.Runtime.Settings;
using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Skips the test unless the browsers run one of the given engines (Browser:Engine, or the engine of
// BrowserStack:Browser), e.g. a check of a Chromium-only feature. Decided at registration, like [RequiresProvider].
public sealed class RequiresEngineAttribute(params BrowserEngine[] engines) : SkipAttribute($"Requires Browser:Engine {string.Join(" or ", engines)}.")
{
    public IReadOnlyList<BrowserEngine> Engines { get; } = engines;

    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(UiAutomationSession.LoadedSettings switch
        {
            null => false,
            { IsConfigured: false } or { IsReplay: true } => true,
            var settings => !Engines.Contains(settings.EffectiveEngine),
        });
    }

    protected override string GetSkipReason(TestRegisteredContext context)
    {
        var engines = string.Join(" or ", Engines.Select(engine => engine.ToString().ToLowerInvariant()));
        return UiAutomationSession.SkipReason
            ?? $"Requires the {engines} engine; the run uses {UiAutomationSession.LoadedSettings?.EffectiveEngine.ToString().ToLowerInvariant()}.";
    }
}
