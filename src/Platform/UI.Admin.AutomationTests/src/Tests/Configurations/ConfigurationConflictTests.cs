using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Save Conflicts: a save sends a JSON Patch whose test operations guard every value it replaces or removes, so another
// writer's change of such a value since the editor loaded opens "Someone else changed this configuration" (Overwrite,
// Reload, discard mine, Copy mine), while a change of another value merges. The other writer is the mock's hook on
// the mock, a second page of the same sign-in on a deployed target.
[Area(Area.Configurations)]
public sealed class ConfigurationConflictTests : PlatformTest
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    // CFG-CON-01: A concurrent change of the same value opens the conflict dialog, and Overwrite saves mine.
    // While this editor holds count 2 unsaved, another writer stores count 7 (v3); the save's test of count 1 fails,
    // the dialog compares theirs with yours (labelled side by side from 1024 px), and Overwrite stores mine as v4.
    [Test]
    [TestCase(125)]
    [UserRole(Role.Editor)]
    public async Task Configurations_ConcurrentChange_OverwriteSavesMine()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""");

        await configuration.Document.ReplaceAsync("""{"count": 2}""");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await ChangeAsAnotherWriterAsync(key, new Dictionary<string, object> { ["count"] = 7 }, 3);
        await Expect(configuration.Editor).ToContainTextAsync("\"count\": 2");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await configuration.Document.PressSaveAsync();
        var conflict = configuration.ConflictDialog;
        await conflict.ExpectOpenAsync();
        await Expect(conflict.Description).ToBeVisibleAsync();
        await ExpectSideLabelsAsync(conflict);

        await conflict.OverwriteAsync();
        await Expect(App.Toast("Configuration saved (v4)")).ToBeVisibleAsync();

        var effective = await configuration.OpenEffectiveAsync();
        await Expect(effective).ToContainTextAsync("\"count\": 2");
        await Expect(effective).Not.ToContainTextAsync("\"count\": 7");
    }

    // CFG-CON-02: Reload, discard mine takes the stored version, and Copy mine copies mine.
    // In the conflict dialog Copy mine copies the edited document (2-space JSON) and reads "Copied"; Reload, discard
    // mine puts the other writer's document (v3) into the editor, with nothing left to save.
    [Test]
    [TestCase(140)]
    [UserRole(Role.Editor)]
    public async Task Configurations_ConcurrentChange_ReloadTakesTheirs()
    {
        await InstallClipboardRecorderAsync();
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""");

        await configuration.Document.ReplaceAsync("""{"count": 2}""");
        await ChangeAsAnotherWriterAsync(key, new Dictionary<string, object> { ["count"] = 7 }, 3);
        await configuration.Document.PressSaveAsync();
        var conflict = configuration.ConflictDialog;
        await conflict.ExpectOpenAsync();

        await conflict.CopyMineButton.ClickAsync();
        await Expect(conflict.CopiedButton).ToBeVisibleAsync();
        await WaitForCopiedTextAsync("{\n  \"count\": 2\n}");

        await conflict.ReloadAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"count\": 7");
        await Expect(configuration.ChangeCounter).ToHaveCountAsync(0);
        await Expect(configuration.SaveButton).ToBeDisabledAsync();
        await Expect(configuration.VersionBadge(3)).ToBeVisibleAsync();
    }

    // CFG-CON-03: A concurrent change of another value merges without a conflict.
    // Only replaced and removed values are guarded, so another writer's new key does not conflict with this editor's
    // replace of count: the save stores v4 without the dialog, and Effective has both changes.
    [Test]
    [TestCase(150)]
    [UserRole(Role.Editor)]
    public async Task Configurations_ConcurrentChangeElsewhere_MergesWithoutConflict()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""");

        await configuration.Document.ReplaceAsync("""{"count": 2}""");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await ChangeAsAnotherWriterAsync(key, new Dictionary<string, object> { ["note"] = "theirs" }, 3);
        await Expect(configuration.Editor).ToContainTextAsync("\"count\": 2");
        await Expect(configuration.Editor).Not.ToContainTextAsync("\"note\"");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await configuration.SaveAsync(4);
        await Expect(configuration.ConflictDialog.Root).ToHaveCountAsync(0);

        var effective = await configuration.OpenEffectiveAsync();
        await Expect(effective).ToContainTextAsync("\"count\": 2");
        await Expect(effective).ToContainTextAsync("\"note\": \"theirs\"");
    }

    // The diff of theirs and yours is side by side with its labels from 1024 px, unified without them below.
    private async Task ExpectSideLabelsAsync(ConfigurationConflictDialog conflict)
    {
        if (Page.ViewportSize is not { Width: < VersionsView.SplitDiffWidth })
        {
            await Expect(conflict.TheirsLabel).ToBeVisibleAsync();
            await Expect(conflict.YoursLabel).ToBeVisibleAsync();
        }
        else
        {
            await Expect(conflict.TheirsLabel).ToHaveCountAsync(0);
            await Expect(conflict.YoursLabel).ToHaveCountAsync(0);
        }
    }

    // Another writer stores the changes over the configuration's top-level values as the next version (expected): on
    // the mock with its window.storytellerMock hook (as Grace Hopper; a second page has data of its own there), on a
    // deployed target in a second page of the same sign-in, which opens the configuration, merges the changes into its
    // JSON and saves. This page does not know of the change until it reads the configuration again.
    private async Task ChangeAsAnotherWriterAsync(string key, Dictionary<string, object> changes, int expectedVersion)
    {
        if (Target.IsMock)
        {
            await MockApi.ChangeConfigurationAsync(key, changes);
            return;
        }

        var writer = await OpenSecondPageAsync();
        try
        {
            var configuration = new ConfigurationDetail(writer, Routes);
            await configuration.OpenAsync(key);
            await configuration.JsonMode.ClickAsync();
            await Expect(configuration.Editor).ToBeVisibleAsync();
            var document = JsonNode.Parse(await configuration.Document.TextAsync())!.AsObject();
            foreach (var (name, value) in changes)
            {
                document[name] = JsonSerializer.SerializeToNode(value);
            }

            await configuration.SetDocumentAsync(document.ToJsonString(IndentedJson));
            await configuration.SaveAsync(expectedVersion);
        }
        finally
        {
            await writer.Page.CloseAsync();
        }

        await Page.BringToFrontAsync();
    }
}
