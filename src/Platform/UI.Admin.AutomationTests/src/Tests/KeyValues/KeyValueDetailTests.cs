using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Detail: the Value tab of a public key, edited in place with Save and Ctrl+S, its metadata and the binding source
// with its copy button, the unsaved-changes guard before the Versions tab, and a concurrent change answered with
// Reload or Overwrite.
[Area(Area.KeyValues)]
public sealed partial class KeyValueDetailTests : PlatformTest
{
    // KV-DET-01: Edit a public value in place and save it.
    // Save is enabled only while the value differs; Save and Ctrl/Cmd+S in the textbox each save a new version, which a
    // page load still shows, and the Versions tab lists all three with only the older ones expiring.
    [Test]
    [TestCase(48)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_EditPublicValue_SavesNewVersions()
    {
        var key = PlatformData.KeyValueKey();
        var first = PlatformData.Slug("value");
        var second = PlatformData.Slug("value");
        var third = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, first);

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.SaveButton).ToBeDisabledAsync();
        await Expect(detail.UnsavedChanges).ToBeHiddenAsync();

        await detail.Value.FillAsync(second);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();
        await Expect(detail.SaveButton).ToBeEnabledAsync();

        await detail.SaveAsync(2);
        await Expect(detail.UnsavedChanges).ToBeHiddenAsync();
        await Expect(detail.SaveButton).ToBeDisabledAsync();
        await Expect(detail.Version).ToHaveTextAsync("v2");

        await detail.Value.FillAsync(third);
        await detail.SaveWithKeyboardAsync(detail.Value, 3);
        await Expect(detail.Version).ToHaveTextAsync("v3");

        await detail.OpenAsync();
        await Expect(detail.Value).ToHaveValueAsync(third);
        await Expect(detail.Version).ToHaveTextAsync("v3");

        await detail.OpenVersionsAsync();
        await Expect(detail.Versions.VersionCells).ToHaveTextAsync(["v3(current)", "v2", "v1"]);
        await Expect(detail.Versions.Cell(3, "Expires")).ToHaveTextAsync("—");
        await Expect(detail.Versions.Cell(2, "Expires")).ToHaveTextAsync(Digit());
        await Expect(detail.Versions.Cell(1, "Expires")).ToHaveTextAsync(Digit());
    }

    // KV-DET-02: The metadata and the binding source of a key.
    // A new public key shows its level, version, author (the signed-in user), creation and content type; the binding
    // follows the binding source, its copy button copies the shown binding (no secret toast for a binding), and the
    // header's copy button copies the key.
    [Test]
    [TestCase(49)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_BindingSource_ChangesCopiedBinding()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        await InstallClipboardRecorderAsync();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, value);

        var detail = new KeyValueDetail(App, Routes, key);
        var author = await App.SignedInNameAsync();
        await Expect(detail.LevelBadge).ToHaveTextAsync("Public");
        await Expect(detail.Version).ToHaveTextAsync("v1");
        await Expect(detail.Author).ToHaveTextAsync(author);
        await Expect(detail.Created).ToHaveTextAsync(new Regex($@"^\s*{Regex.Escape(author)},\s+\S"));
        await Expect(detail.ContentType).ToHaveTextAsync("text/plain");

        await Expect(detail.BindingSource).ToHaveTextAsync("default source");
        await Expect(detail.Binding).ToHaveTextAsync($"@{detail.Key}");

        await detail.PickBindingSourceAsync("public only");
        await Expect(detail.Binding).ToHaveTextAsync($"@public({detail.Key})");
        await detail.BindingCopyButton.ClickAsync();
        await WaitForCopiedTextAsync($"@public({detail.Key})");
        await Expect(App.Toast("The clipboard contains a secret")).ToBeHiddenAsync();

        await detail.PickBindingSourceAsync("secret only");
        await Expect(detail.Binding).ToHaveTextAsync($"@secret({detail.Key})");
        await detail.PickBindingSourceAsync("default source");
        await Expect(detail.Binding).ToHaveTextAsync($"@{detail.Key}");

        await detail.KeyCopyButton.ClickAsync();
        await WaitForCopiedTextAsync(detail.Key);
    }

    // KV-DET-03: Unsaved changes ask before switching to Versions.
    // A changed value marks the page title with "● "; the Versions tab asks "Discard changes?": Keep editing stays on
    // the Value tab with the draft, Discard opens Versions, and the Value tab shows the saved value again.
    [Test]
    [TestCase(51)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_UnsavedChanges_AskBeforeVersions()
    {
        var key = PlatformData.KeyValueKey();
        var saved = PlatformData.Slug("value");
        var draft = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, saved);

        var detail = new KeyValueDetail(App, Routes, key);
        await detail.Value.FillAsync(draft);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();
        await Expect(App.Page).ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        var discard = new DiscardChangesDialog(App);
        await detail.VersionsTab.ClickAsync();
        await discard.ExpectOpenAsync();
        await Expect(discard.Description).ToHaveTextAsync(DiscardChangesDialog.DescriptionText);
        await Expect(discard.KeepEditingButton).ToBeVisibleAsync();
        await Expect(discard.DiscardButton).ToBeVisibleAsync();

        await discard.KeepEditingAsync();
        await App.ExpectAtAsync(Routes.KeyValue(key));
        await Expect(detail.Value).ToHaveValueAsync(draft);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();

        await detail.VersionsTab.ClickAsync();
        await discard.DiscardAsync();
        await App.ExpectAtAsync(Routes.KeyValueVersions(key));
        await Expect(App.Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        await detail.OpenValueAsync();
        await Expect(detail.Value).ToHaveValueAsync(saved);
        await Expect(detail.UnsavedChanges).ToBeHiddenAsync();
        await Expect(detail.SaveButton).ToBeDisabledAsync();
    }

    // KV-DET-04: A concurrent change offers Reload or Overwrite.
    // Another writer saves the key while this page holds a draft: the save sends the loaded version and gets the alert
    // "Someone else changed this key" with the newer version and its author. Reload takes the other value; after a
    // second concurrent change, Overwrite saves the draft as the newest version over both.
    [Test]
    [TestCase(53)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_ConcurrentChange_OffersReloadOrOverwrite()
    {
        var key = PlatformData.KeyValueKey();
        var original = PlatformData.Slug("value");
        var theirs = PlatformData.Slug("value");
        var draft = PlatformData.Slug("value");
        var theirsAgain = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, original);

        var detail = new KeyValueDetail(App, Routes, key);
        var author = await App.SignedInNameAsync();
        await detail.Value.FillAsync(draft);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();

        await WriteConcurrentlyAsync(detail.Key, theirs, 2);
        await Expect(detail.Value).ToHaveValueAsync(draft);
        await Expect(detail.Version).ToHaveTextAsync("v1");

        await detail.SaveButton.ClickAsync();
        await Expect(detail.ConflictAlert).ToBeVisibleAsync();
        await Expect(detail.ConflictDescription).ToHaveTextAsync($"It is at version 2 now ({author}). Reload to see it, or overwrite with your value.");
        await Expect(detail.ReloadButton).ToBeVisibleAsync();
        await Expect(detail.OverwriteButton).ToBeVisibleAsync();
        await Expect(App.Toast(SavedToast())).ToBeHiddenAsync();

        await detail.ReloadButton.ClickAsync();
        await Expect(detail.Value).ToHaveValueAsync(theirs);
        await Expect(detail.ConflictAlert).ToBeHiddenAsync();
        await Expect(detail.SaveButton).ToBeDisabledAsync();

        await detail.Value.FillAsync(draft);
        await WriteConcurrentlyAsync(detail.Key, theirsAgain, 3);
        await Expect(detail.Value).ToHaveValueAsync(draft);

        await detail.SaveButton.ClickAsync();
        await Expect(detail.ConflictDescription).ToContainTextAsync("It is at version 3 now");

        await detail.OverwriteButton.ClickAsync();
        await Expect(App.Toast("Saved as version 4")).ToBeVisibleAsync();
        await Expect(detail.ConflictAlert).ToBeHiddenAsync();
        await Expect(detail.Value).ToHaveValueAsync(draft);
        await Expect(detail.Version).ToHaveTextAsync("v4");

        await detail.OpenVersionsAsync();
        await Expect(detail.Versions.VersionCells).ToHaveTextAsync(["v4(current)", "v3", "v2", "v1"]);
    }

    // The other writer of the key: on the mock a PUT of the value without an expected version from inside the page
    // (MSW keeps it in this tab's data); on a deployed target a second page of the context that saves it through its
    // own detail. Neither reloads nor focuses this page, whose detail keeps the version it loaded.
    private async Task WriteConcurrentlyAsync(string key, string value, int expectedVersion)
    {
        if (Target.IsMock)
        {
            var response = await MockApi.FetchAsync(
                "PUT",
                MockApi.ViewPath($"key-values/{key}"),
                new Dictionary<string, string> { ["Value"] = value });
            var version = response.IsSuccess ? response.Json()?["Version"]?.GetValue<int>() : null;
            if (version != expectedVersion)
            {
                throw new InvalidOperationException($"The concurrent write of '{key}' answered {response.Status} {response.Body}, not version {expectedVersion}.");
            }

            return;
        }

        var other = await OpenSecondPageAsync();
        var otherDetail = new KeyValueDetail(other, Routes, key);
        await otherDetail.OpenAsync();
        await otherDetail.Value.FillAsync(value);
        await otherDetail.SaveAsync(expectedVersion);
        await other.Page.CloseAsync();
    }

    [GeneratedRegex(@"\d")]
    private static partial Regex Digit();

    [GeneratedRegex("^Saved as version ")]
    private static partial Regex SavedToast();
}
