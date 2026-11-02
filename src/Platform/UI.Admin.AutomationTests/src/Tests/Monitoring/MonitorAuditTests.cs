using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Monitoring;

// Audit Trail: the Audit tab of Monitor with the events the test produces itself, never the target's other events or
// the mock's seed. In the target view a Standard secret is created and revealed; in a disposable project the secrets
// policy, the Standard key and the recipients change and the values are exported with secret values. A deployed
// target's audit is eventually consistent, so there its first read of an event polls for up to a minute
// (AuditTrail.WaitForKeyEventsAsync, WaitForOperationEventsAsync with AuditPolling); on the mock it is one attempt.
// Nothing is cleaned up: the keys carry the run id, and a deletion would only add an event.
[Area(Area.Monitoring)]
public sealed partial class MonitorAuditTests : PlatformTest
{
    private const string Standard = "Standard";

    // One attempt on the mock, which answers at once; polling for up to a minute on a deployed target.
    private Eventually AuditPolling => Eventually.On(Target.IsMock);

    // MON-AUD-01: Creating and revealing a secret is recorded in the audit trail.
    // A new Standard secret in the target view, revealed once, has two events, newest first: Reveal, then Create with
    // the view, the key, v1, the level Standard, the channel User and the count 1. The Reveal's copy button copies the
    // whole correlation id, of which the row shows the start, and its actor, the signed-in user, links to the Members
    // page, which marks the user's own row.
    [Test]
    [TestCase(25)]
    [UserRole(Role.Admin)]
    public async Task MonitorAudit_CreateAndRevealSecret_RecordsBothEvents()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        await InstallClipboardRecorderAsync();

        var keyValues = new KeyValuesPage(App, Routes);
        await CreateAndRevealSecretAsync(keyValues, key, value);

        var audit = new AuditTrail(App, Routes);
        await audit.WaitForKeyEventsAsync(key, 2, AuditPolling);
        var reveal = audit.Rows.Nth(0);
        var create = audit.Rows.Nth(1);
        await Expect(AuditTrail.Cell(reveal, AuditTrail.OperationColumn)).ToContainTextAsync("Reveal");
        await Expect(AuditTrail.Cell(create, AuditTrail.OperationColumn)).ToContainTextAsync("Create");

        await Expect(AuditTrail.Cell(create, AuditTrail.ViewColumn)).ToHaveTextAsync(Routes.View);
        await Expect(AuditTrail.Cell(create, AuditTrail.KeyColumn)).ToHaveTextAsync(key.ToLowerInvariant());
        await Expect(AuditTrail.Cell(create, AuditTrail.VersionColumn)).ToHaveTextAsync("v1");
        await Expect(AuditTrail.Cell(create, AuditTrail.LevelColumn)).ToHaveTextAsync(Standard);
        await Expect(AuditTrail.Cell(create, AuditTrail.ChannelColumn)).ToHaveTextAsync("User");
        await Expect(AuditTrail.Cell(create, AuditTrail.CountColumn)).ToHaveTextAsync("1");
        await Expect(AuditTrail.ActorLink(create)).ToBeVisibleAsync();

        var prefix = await AuditTrail.CorrelationPrefixAsync(reveal);
        await AuditTrail.CopyCorrelationButton(reveal).ClickAsync();
        await Page.WaitForFunctionAsync("() => (window.__copied ?? []).length > 0", null, new() { PollingInterval = 100 });
        var copied = await CopiedTextAsync() ?? string.Empty;
        await Assert.That(prefix.Length).IsEqualTo(8);
        await Assert.That(copied.Length).IsGreaterThan(8);
        await Assert.That(copied).StartsWith(prefix);

        // The actor is the signed-in user: the link highlights the user's own account id, and only that row.
        await AuditTrail.ActorLink(reveal).ClickAsync();
        await App.ExpectAtAsync(Routes.Members);
        var members = new MembersPage(App, Routes).Members;
        await Expect(members.OwnRow).ToHaveAttributeAsync("aria-current", "true");
        await Expect(members.HighlightedRows).ToHaveCountAsync(1);
        await App.ExpectQueryAsync("highlight", await members.OwnAccountIdAsync());
    }

    // MON-AUD-02: Audit filters narrow the events by operation, view and dates.
    // The Create and the Reveal of a new Standard secret, found by their key, narrow to one event per operation, stay
    // two in the target view, are gone in a view that does not exist ("No audit events" with its description) and up
    // to yesterday, and are back from today on (the audit's dates are UTC days, To includes its whole day). The target
    // view and From today list the same two events as before, so those checks wait for the page's read with the filter.
    [Test]
    [TestCase(29)]
    [UserRole(Role.Admin)]
    public async Task MonitorAudit_ApplyFilters_NarrowsEvents()
    {
        await InstallRequestRecorderAsync();

        // Before the secret exists: its events are from today (UTC) on, even when midnight passes during the test.
        var dates = UtcDates.Now();
        var missingView = PlatformData.Slug("view");
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await CreateAndRevealSecretAsync(keyValues, key, value);

        var audit = new AuditTrail(App, Routes);
        await audit.WaitForKeyEventsAsync(key, 2, AuditPolling);
        await Expect(AuditTrail.Cell(audit.Rows.Nth(0), AuditTrail.OperationColumn)).ToContainTextAsync("Reveal");
        await Expect(AuditTrail.Cell(audit.Rows.Nth(1), AuditTrail.OperationColumn)).ToContainTextAsync("Create");
        await Expect(audit.KeyFilter).ToHaveValueAsync(key.ToLowerInvariant());

        // A check starts with what tells the new result from the one before; where both list the same events, it
        // starts with the page's read of the filter (the audit lists nothing while it reads a new filter).
        await audit.PickOperationAsync("Reveal");
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(1));
        await ExpectOnlyOperationAsync(audit, "Reveal");

        await audit.PickOperationAsync("Create");
        await ExpectOnlyOperationAsync(audit, "Create");
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(1));

        await audit.PickOperationAsync(AuditTrail.EveryOperation);
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(2));
        await audit.ViewFilter.FillAsync(Routes.View);
        await WaitForAuditReadAsync("view", $"^{Regex.Escape(Routes.View)}$");
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(2));
        await Expect(audit.Rows).ToHaveCountAsync(2);
        await Expect(AuditTrail.Cell(audit.Rows.Nth(0), AuditTrail.ViewColumn)).ToHaveTextAsync(Routes.View);
        await Expect(AuditTrail.Cell(audit.Rows.Nth(1), AuditTrail.ViewColumn)).ToHaveTextAsync(Routes.View);

        await audit.ViewFilter.FillAsync(missingView);
        await ExpectNoEventsAsync(audit);

        await audit.ViewFilter.FillAsync(string.Empty);
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(2));
        await audit.To.FillAsync(UtcDates.Iso(dates.Yesterday));
        await ExpectNoEventsAsync(audit);

        await audit.To.FillAsync(string.Empty);
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(2));
        await audit.From.FillAsync(UtcDates.Iso(dates.Today));
        await WaitForAuditReadAsync("from", $"^{UtcDates.Iso(dates.Today)}T");
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(2));
        await Expect(audit.Rows).ToHaveCountAsync(2);
    }

    // MON-AUD-03: Project-wide secret changes and an export with secret values are recorded in the audit trail.
    // In a disposable project an Owner turns off the mTLS rule, creates a Standard secret, rotates the Standard key,
    // adds a HYOK recipient and exports the values with secret values; the audit lists each once under its operation:
    // PolicyChange, Rotate with the new key version, RecipientAdded with the recipient's id and Export of the default
    // view, each by the user (channel User) with a link to the member.
    [Test]
    [TestCase(32)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task MonitorAudit_ProjectSecretChanges_AreRecorded()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        var recipientName = PlatformData.Title();
        var recipientId = PlatformData.RecipientId();
        var publicKey = PlatformData.RsaPublicKeyPem(3072);

        var project = await CreateDisposableProjectAsync();
        var settings = new ProjectSettingsPage(App, project);
        var levels = new AllowedLevelsCard(App, project);

        // Saved first, while the new project's policy is at its first version: a deployed API caches the policy per
        // instance for up to a minute, and a save with a stale version is refused.
        await settings.OpenAsync("secrets");
        await Expect(levels.MtlsSwitch).ToBeCheckedAsync();
        await levels.MtlsSwitch.SetCheckedAsync(false);
        await levels.SaveButton.ClickAsync();
        await levels.MtlsDialog.ConfirmAsync("Turn off the mTLS rule");
        await Expect(App.Toast("Secrets policy saved")).ToBeVisibleAsync();
        await Expect(levels.MtlsSwitch).Not.ToBeCheckedAsync();
        await Expect(levels.SaveButton).ToBeDisabledAsync();

        // On a deployed target the first secret creates the project's Standard key, so it comes before the rotation.
        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, value, Standard);
        await Expect(keyValues.LevelBadge(key)).ToHaveTextAsync(Standard);
        await Expect(keyValues.SecretValue(key)).ToHaveTextAsync(KeyValuesPage.MaskedValue);

        var version = await RotateStandardKeyAsync(settings);

        await settings.AddRecipientAsync(recipientName, recipientId, publicKey);
        await Expect(settings.Recipient(recipientId)).ToContainTextAsync("RSA 3072 bits");
        await Expect(settings.Recipient(recipientId)).ToContainTextAsync("enabled");

        // The file holds the secret value: only its name is checked, and it is never kept.
        await keyValues.OpenAsync();
        var exportMenu = new KeyValueExportMenu(App);
        await exportMenu.OpenAsync();
        await exportMenu.IncludeSecretValues.ClickAsync();
        await Expect(exportMenu.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "true");
        var file = await DownloadAsync(() => exportMenu.Format(KeyValueExportMenu.FlatJson).ClickAsync(), 30_000);
        await Expect(App.Toast("The file contains secret values")).ToBeVisibleAsync();
        await Expect(App.Notifications.GetByText("Store it where it belongs, then delete it.", new() { Exact = true }).Last).ToBeVisibleAsync();
        await Assert.That(file.SuggestedFilename).IsEqualTo($"{project.Organization}-{project.Project}-{AppRoutes.DefaultView}.flat.json");

        // A new project's audit is empty and each change is made once, so each operation has exactly one event. The
        // details differ between the mock and the API, so they are matched with one pattern for both.
        var audit = new AuditTrail(App, project);
        var memberLink = new Regex($"/{Regex.Escape(project.Organization)}/{Regex.Escape(project.Project)}/members\\?highlight=.+");

        await audit.WaitForOperationEventsAsync("PolicyChange", 1, AuditPolling);
        var policyChange = audit.Rows.First;
        await Expect(AuditTrail.Cell(policyChange, AuditTrail.OperationColumn)).ToContainTextAsync(PolicyChangeDetail());
        await ExpectByUserAsync(policyChange, memberLink);

        await audit.WaitForOperationEventsAsync("Rotate", 1, AuditPolling);
        var rotate = audit.Rows.First;
        await Expect(AuditTrail.Cell(rotate, AuditTrail.OperationColumn)).ToContainTextAsync(version);
        await ExpectByUserAsync(rotate, memberLink);

        await audit.WaitForOperationEventsAsync("RecipientAdded", 1, AuditPolling);
        var recipientAdded = audit.Rows.First;
        await Expect(AuditTrail.Cell(recipientAdded, AuditTrail.OperationColumn)).ToContainTextAsync(recipientId);
        await ExpectByUserAsync(recipientAdded, memberLink);

        await audit.WaitForOperationEventsAsync("Export", 1, AuditPolling);
        var export = audit.Rows.First;
        await Expect(AuditTrail.Cell(export, AuditTrail.ViewColumn)).ToHaveTextAsync(AppRoutes.DefaultView);
        await Expect(AuditTrail.Cell(export, AuditTrail.KeyColumn)).ToHaveTextAsync(string.Empty);
        await Expect(AuditTrail.Cell(export, AuditTrail.OperationColumn)).ToContainTextAsync(ExportDetail());
        await ExpectByUserAsync(export, memberLink);
    }

    // Opens the values store, creates the Standard secret (its detail with the level's badge and the masked value) and
    // reveals it (the value, masked again after 60 seconds).
    private static async Task CreateAndRevealSecretAsync(KeyValuesPage keyValues, string key, string value)
    {
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, value, Standard);
        await Expect(keyValues.LevelBadge(key)).ToHaveTextAsync(Standard);
        await Expect(keyValues.SecretValue(key)).ToHaveTextAsync(KeyValuesPage.MaskedValue);

        var revealed = await keyValues.RevealAsync(key);
        await Expect(revealed).ToHaveTextAsync(value);
        await Expect(keyValues.MaskedAgainHint(key)).ToBeVisibleAsync();
    }

    // The only listed event is of the operation.
    private static async Task ExpectOnlyOperationAsync(AuditTrail audit, string operation)
    {
        await Expect(AuditTrail.Cell(audit.Rows.First, AuditTrail.OperationColumn)).ToContainTextAsync(operation);
        await Expect(audit.Rows).ToHaveCountAsync(1);
    }

    // The empty state of the filters: no event, with the description of what the audit records.
    private static async Task ExpectNoEventsAsync(AuditTrail audit)
    {
        await Expect(audit.NoEvents).ToBeVisibleAsync();
        await Expect(audit.NoEventsText).ToBeVisibleAsync();
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(0));
    }

    // The event is the user's: the channel User and the actor as a link to the member on the project's Members page.
    private static async Task ExpectByUserAsync(ILocator row, Regex memberLink)
    {
        await Expect(AuditTrail.Cell(row, AuditTrail.ChannelColumn)).ToHaveTextAsync("User");
        await Expect(AuditTrail.ActorLink(row)).ToHaveAttributeAsync("href", memberLink);
    }

    // A key version other than the noted one: a non-blank text, so that a row without a version cannot pass.
    private static Regex OtherVersion(string noted)
    {
        return new Regex($@"^(?!{Regex.Escape(noted)}$)\S+$");
    }

    // Rotates the Standard key in Key-encryption keys and returns its new version, once the card shows it. A deployed
    // API may serve the policy from an instance's cache for up to a minute: right after the first secret a page load
    // may show no key yet (no "Rotate now"), and the card's read after the rotation may show the old version, so there
    // OpenUntilPolicyShowsAsync loads the settings again until they show. On the mock both show at once, and the
    // card's own update after "Rotate" is checked without a page load.
    private async Task<string> RotateStandardKeyAsync(ProjectSettingsPage settings)
    {
        var keys = new KeyEncryptionKeysCard(App);
        await OpenUntilPolicyShowsAsync(
            () => settings.ReloadAsync("secrets"),
            async () => await Expect(keys.RotateButton(Standard)).ToBeVisibleAsync());
        var noted = await keys.CurrentVersionAsync(Standard);

        await keys.RotateButton(Standard).ClickAsync();
        await keys.RotateDialog(Standard).ConfirmAsync("Rotate");
        await Expect(App.Toast("Standard key rotated")).ToBeVisibleAsync();

        // The first check is of the card as the rotation left it; only a later attempt (a deployed target) reloads.
        var loads = 0;
        await OpenUntilPolicyShowsAsync(
            () => loads++ == 0 ? Task.CompletedTask : settings.ReloadAsync("secrets"),
            async () => await Expect(keys.CurrentVersion(Standard)).ToHaveTextAsync(OtherVersion(noted)));
        return await keys.CurrentVersionAsync(Standard);
    }

    // Waits until the page has read the audit with the filter: a request whose query parameter matches the pattern (a
    // JavaScript regular expression), among the requests the recorder holds since the last page load.
    private async Task WaitForAuditReadAsync(string parameter, string pattern)
    {
        await Page.WaitForFunctionAsync(
            """
            ([parameter, pattern]) => (window.__requests ?? []).some((request) => {
              const url = new URL(request.url);
              return url.pathname.endsWith('/key-values/audit') && new RegExp(pattern).test(url.searchParams.get(parameter) ?? '');
            })
            """,
            new[] { parameter, pattern },
            new() { PollingInterval = 100 });
    }

    // The mock lists the allowed levels, the API the changes of the save.
    [GeneratedRegex("Allowed: Public, Standard|default=Standard, mtls=False")]
    private static partial Regex PolicyChangeDetail();

    // The mock counts the secrets revealed, the API the secret values.
    [GeneratedRegex(@"1 secrets revealed|1 secret value\(s\)")]
    private static partial Regex ExportDetail();
}
