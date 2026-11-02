using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Edit and Versions: editing the own document in JSON and YAML (the change counter, Revert, the Discard changes? guard,
// syntax errors and the format switch) and the Versions tab with its timeline, one version and the diffs.
[Area(Area.Configurations)]
public sealed partial class ConfigurationEditTests : PlatformTest
{
    // CFG-EDT-01: Unsaved changes ask before leaving, and Revert drops them.
    // A changed document marks the title with "● " and asks "Discard changes?" before another tab opens (Keep editing
    // stays with the draft); a format switch only changes the query, so it does not ask. Revert restores the saved
    // document, and Discard on the Annotation link leaves to the annotation without the list's filter.
    [Test]
    [TestCase(83)]
    [UserRole(Role.Editor)]
    public async Task Configurations_UnsavedChanges_AskBeforeLeaving()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenFilteredAsync(key, $"key:{key}");
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""");

        await configuration.Document.ReplaceAsync("""{"count": 2, "extra": true}""");
        await Expect(configuration.Changes(2)).ToBeVisibleAsync();
        await Expect(configuration.RevertButton).ToBeEnabledAsync();
        await Expect(configuration.SaveButton).ToBeEnabledAsync();
        await Expect(App.Page).ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        await configuration.EffectiveTab.ClickAsync();
        var discard = new DiscardChangesDialog(App);
        await discard.ExpectOpenAsync();
        await Expect(discard.Description).ToHaveTextAsync(DiscardChangesDialog.DescriptionText);
        await App.ExpectAtAsync(Routes.Configuration(key));

        await discard.KeepEditingAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"extra\": true");

        await configuration.YamlMode.ClickAsync();
        await App.ExpectQueryAsync("format", "yaml");
        await Expect(discard.Root).ToHaveCountAsync(0);
        await Expect(configuration.Editor).ToContainTextAsync("extra: true");
        await Expect(configuration.Changes(2)).ToBeVisibleAsync();

        // By the keyboard: the save's toast can still cover the footer's buttons at the bottom right, and a pointer on
        // a toast keeps it open.
        await configuration.RevertButton.PressAsync("Enter");
        await Expect(configuration.Editor).ToContainTextAsync("count: 1");
        await Expect(configuration.Editor).Not.ToContainTextAsync("extra");
        await Expect(configuration.ChangeCounter).ToHaveCountAsync(0);
        await Expect(configuration.RevertButton).ToBeDisabledAsync();
        await Expect(configuration.SaveButton).ToBeDisabledAsync();
        await Expect(App.Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        await configuration.Document.ReplaceAsync("count: 3");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();
        await configuration.AnnotationLink.ClickAsync();
        await discard.DiscardAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await App.ExpectQueryAsync("q", null);
        await Expect(App.Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());
    }

    // CFG-EDT-02: A syntax error disables Save and blocks the format switch.
    // A JSON syntax error shows "Line 1: Value expected" under the editor and keeps the format (a warning toast); an
    // empty text is an error too. A valid document with an integer beyond 2^53 is saved as it is, with a warning about
    // the precision, and counts as a change again.
    [Test]
    [TestCase(95)]
    [UserRole(Role.Editor)]
    public async Task Configurations_SyntaxError_BlocksSaveAndFormatSwitch()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        await configuration.SetDocumentAsync("""{"count": }""");
        await Expect(configuration.ParseError).ToHaveTextAsync("Line 1: Value expected");
        await Expect(configuration.SaveButton).ToBeDisabledAsync();

        await configuration.YamlMode.ClickAsync();
        await Expect(App.Toast("Fix the syntax error first")).ToBeVisibleAsync();
        await Expect(configuration.JsonMode).ToBeCheckedAsync();
        await App.ExpectQueryAsync("format", "json");

        await configuration.Document.ReplaceAsync(string.Empty);
        await Expect(configuration.ParseError).ToHaveTextAsync("Line 1: The document is empty.");
        await Expect(configuration.SaveButton).ToBeDisabledAsync();

        await configuration.Document.ReplaceAsync("""{"big": 12345678901234567890}""");
        await Expect(configuration.ParseError).ToHaveCountAsync(0);
        await Expect(configuration.UnsafeNumberWarning).ToBeVisibleAsync();
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();
        await Expect(configuration.SaveButton).ToBeEnabledAsync();
    }

    // CFG-EDT-03: YAML mode edits and saves the document, and the format stays in the URL.
    // The Edit tab's format lives in ?format=: YAML shows the saved document as YAML, a YAML edit saves as the next
    // version, JSON shows the same values, and a page loaded with ?format=yaml opens in YAML.
    [Test]
    [TestCase(109)]
    [UserRole(Role.Editor)]
    public async Task Configurations_YamlMode_SavesAndKeepsFormat()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""");

        await configuration.YamlMode.ClickAsync();
        await App.ExpectQueryAsync("format", "yaml");
        await Expect(configuration.Editor).ToContainTextAsync("count: 1");

        await configuration.Document.ReplaceAsync("count: 2\nlabel: from-yaml");
        await Expect(configuration.Changes(2)).ToBeVisibleAsync();

        await configuration.SaveAsync(3);

        await configuration.JsonMode.ClickAsync();
        await App.ExpectQueryAsync("format", "json");
        await Expect(configuration.Editor).ToContainTextAsync("\"count\": 2");
        await Expect(configuration.Editor).ToContainTextAsync("\"label\": \"from-yaml\"");

        await App.GotoAsync(AppRoutes.WithQuery(Routes.Configuration(key), ("format", "yaml")));
        await Expect(configuration.YamlMode).ToBeCheckedAsync();
        await Expect(configuration.Editor).ToContainTextAsync("count: 2");
    }

    // CFG-EDT-04: Versions lists every save and compares two versions.
    // Three versions (Create empty, two saves) are listed newest first, the newest "current"; Compare with previous
    // shows the diff of v2 and v3 (side by side with their labels from 1024 px), View shows one version, the pickers
    // refuse the same version twice and compare two picked ones.
    [Test]
    [TestCase(123)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Versions_CompareShowsDiff()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"count": 1}""", """{"count": 2}""");

        await configuration.OpenTabAsync("Versions");
        var versions = configuration.Versions;
        await versions.WaitForTimelineAsync();
        await Expect(versions.Rows).ToHaveCountAsync(3);
        for (var index = 0; index < 3; index++)
        {
            var version = 3 - index;
            var row = versions.Rows.Nth(index);
            await Expect(row.GetByText($"v{version}", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(row.GetByRole(AriaRole.Button, new() { Name = "View", Exact = true })).ToBeVisibleAsync();
            await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Compare with previous", Exact = true })).ToHaveCountAsync(version > 1 ? 1 : 0);
        }

        await Expect(versions.CurrentBadge(3)).ToBeVisibleAsync();
        await Expect(versions.CurrentBadge(2)).ToHaveCountAsync(0);
        await Expect(versions.CurrentBadge(1)).ToHaveCountAsync(0);

        await versions.CompareWithPreviousAsync(3);
        await App.ExpectQueryAsync("from", "2");
        await Expect(versions.CompareHeader(2, 3)).ToBeVisibleAsync();
        await Expect(versions.DiffSummary).ToHaveTextAsync(OneLineChanged());
        if (Page.ViewportSize is not { Width: < VersionsView.SplitDiffWidth })
        {
            await Expect(versions.CompareSideLabel(2)).ToBeVisibleAsync();
            await Expect(versions.CompareSideLabel(3)).ToBeVisibleAsync();
        }
        else
        {
            // Below 1024 px the diff is unified, without the side labels.
            await Expect(versions.CompareDiffView).ToBeVisibleAsync();
            await Expect(versions.CompareSideLabel(2)).ToHaveCountAsync(0);
        }

        await versions.BackToHistoryAsync();
        await Expect(versions.CompareHeader(2, 3)).ToHaveCountAsync(0);
        await Expect(versions.BackToHistoryButton).ToHaveCountAsync(0);
        await Expect(versions.FromSelect).ToBeVisibleAsync();
        await Expect(versions.ToSelect).ToBeVisibleAsync();
        await Expect(versions.Timeline).ToBeVisibleAsync();

        var first = await versions.ViewAsync(1);
        await Expect(versions.ShownVersionTitle(1)).ToBeVisibleAsync();
        await first.ExpectTextAsync("{}");

        await Expect(versions.ToSelect).ToContainTextAsync("v1");
        await versions.PickVersionAsync(versions.FromSelect, 1);
        await Expect(versions.CompareButton).ToBeDisabledAsync();

        await versions.PickVersionAsync(versions.ToSelect, 3);
        await versions.CompareButton.ClickAsync();
        await App.ExpectQueryAsync("v", "3");
        await App.ExpectQueryAsync("from", "1");
        await Expect(versions.CompareHeader(1, 3)).ToBeVisibleAsync();
    }

    // The diff of {"count": 1} and {"count": 2}: one line added, one removed (the minus is U+2212).
    [GeneratedRegex(@"^\+1 −1 · \d+ changes?$")]
    private static partial Regex OneLineChanged();
}
