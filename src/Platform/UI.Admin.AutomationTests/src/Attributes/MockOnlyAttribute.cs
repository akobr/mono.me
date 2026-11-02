using _42.Testing.UI.Automation.Runtime.TUnit;
using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [MockOnly("…")] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// A test that needs the admin UI's mock build (another mock scenario, its seed data or a window.storytellerMock hook):
// skipped at registration unless the target is the mock (PlatformTarget.IsMock), with "Mock only: <why>." as the
// reason. Decided from the settings like the library's [RequiresEngine], so a skipped test runs no hook.
public sealed class MockOnlyAttribute(string why) : SkipAttribute($"Mock only: {why}")
{
    // Why the test needs the mock, e.g. "the new-user scenario".
    public string Why { get; } = why;

    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(UiAutomationSession.LoadedSettings switch
        {
            null => false,
            { IsConfigured: false } or { IsReplay: true } => true,
            var settings => !settings.Bind<PlatformTarget>(PlatformTarget.Section).IsMock,
        });
    }

    protected override string GetSkipReason(TestRegisteredContext context)
    {
        return UiAutomationSession.SkipReason ?? $"Mock only: {Why.TrimEnd('.')}.";
    }
}
