using _42.Testing.UI.Automation.Runtime.TUnit;
using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [RequiresPurge] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// A maintenance test that deletes the automation data of earlier runs (Tests/Maintenance): skipped at registration
// unless the run asks for it with Maintenance:Purge=true, and always on the mock, whose data is gone with its page.
// Decided from the settings like [MockOnly], so a skipped test runs no hook and leases no user.
public sealed class RequiresPurgeAttribute() : SkipAttribute(NotAskedFor)
{
    private const string NotAskedFor = "Maintenance: nothing is deleted unless a run asks for it with --test-parameter Maintenance:Purge=true (or UITESTS_Maintenance__Purge=true).";

    private const string OnTheMock = "Maintenance: the mock keeps no data beyond its page, so there is nothing to purge.";

    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(UiAutomationSession.LoadedSettings switch
        {
            // Not loaded: the session fails every test before it starts (UiAutomationSession.SkipReason), this one
            // included, before it could delete anything; as [MockOnly] does, the failure is not hidden as a skip.
            null => false,
            { IsConfigured: false } or { IsReplay: true } => true,
            var settings => settings.Bind<PlatformTarget>(PlatformTarget.Section).IsMock || !settings.Bind<MaintenanceSettings>(MaintenanceSettings.Section).Purge,
        });
    }

    protected override string GetSkipReason(TestRegisteredContext context)
    {
        var isMock = UiAutomationSession.LoadedSettings?.Bind<PlatformTarget>(PlatformTarget.Section).IsMock == true;
        return UiAutomationSession.SkipReason ?? (isMock ? OnTheMock : NotAskedFor);
    }
}
