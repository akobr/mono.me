namespace _42.Platform.UI.Admin.AutomationTests;

// The Maintenance section: the purge of the automation data of earlier runs (Tests/Maintenance/AutomationDataTests).
// Nothing is deleted unless a run sets Purge (--test-parameter Maintenance:Purge=true or UITESTS_Maintenance__Purge=true).
// Read with Settings.Bind<MaintenanceSettings>("Maintenance"); the defaults are in testsettings.json.
public sealed class MaintenanceSettings
{
    public const string Section = "Maintenance";

    // Whether PurgeAutomationData runs; false (the default everywhere) skips it.
    public bool Purge { get; set; }

    // Only the roots created more than this many hours ago are deleted, and only the deleted roots deleted that long ago
    // (or by the purge itself) are purged.
    public int OlderThanHours { get; set; } = 72;

    // The purge starts no request after this many seconds; what is left, the next run continues.
    public int BudgetSeconds { get; set; } = 120;
}
