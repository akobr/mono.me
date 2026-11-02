using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Lifecycle: Delete, the Deleted keys and restore, in a disposable project (the list must hold only the test's keys,
// and "Select all" selects every listed key). A delete from the row menu restored from the detail, the removal for
// good through Delete permanently and Purge, bulk delete and bulk restore of selected keys, and a New key over a
// deleted key that continues its version numbers. Desktop only: the phone's list has no row menu and no selection.
[Area(Area.KeyValues)]
public sealed partial class KeyValueLifecycleTests : PlatformTest
{
    private const string EmptyActive = "No key values in this view";
    private const string EmptyDeleted = "No deleted keys";
    private const string EmptyDeletedDescription = "Deleted keys can be restored here for 90 days.";

    // KV-LIF-01: Delete a key from the row menu and restore it from the Deleted keys.
    // The row menu's Delete asks first (Cancel keeps the key), then moves the key to the Deleted keys for 90 days; there
    // its detail is read-only with the alert "This key is deleted", and Restore brings back the latest version without
    // a new one.
    [Test]
    [TestCase(164)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_DeleteFromRowMenu_RestoresFromDetail()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, value);
        var detail = new KeyValueDetail(App, project, key);
        await detail.CloseAsync();

        await keyValues.RowActionAsync(key, "Delete");
        var delete = keyValues.DeleteDialog(key);
        await delete.ExpectOpenAsync();
        await Expect(delete.Description).ToHaveTextAsync("The key can be restored for 90 days with every version; after that it is removed for good.");
        await delete.CancelAsync();
        await Expect(keyValues.Item(key)).ToBeVisibleAsync();

        await keyValues.RowActionAsync(key, "Delete");
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast($"'{detail.Key}' deleted")).ToBeVisibleAsync();
        await Expect(ToastText("Restore it from the Deleted keys.")).ToBeVisibleAsync();
        await Expect(keyValues.EmptyState(EmptyActive)).ToBeVisibleAsync();

        await keyValues.DeletedButton.ClickAsync();
        await App.ExpectQueryAsync("state", "Deleted");
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Expires", Exact = true })).ToBeVisibleAsync();
        await Expect(keyValues.ItemCell(key, "Updated")).ToHaveTextAsync("in 90 days");

        await keyValues.OpenItemAsync(key);
        await Expect(detail.DeletedBadge).ToBeVisibleAsync();
        await Expect(detail.DeletedAlert).ToBeVisibleAsync();
        await Expect(detail.DeletedDescription).ToHaveTextAsync("Removed for good in 90 day(s), with every version. Restore it to bring back the latest version.");
        await Expect(detail.Value).ToBeDisabledAsync();
        await Expect(detail.SaveButton).ToHaveCountAsync(0);

        await detail.RestoreButton.ClickAsync();
        await Expect(App.Toast($"'{detail.Key}' restored")).ToBeVisibleAsync();
        await Expect(detail.DeletedAlert).ToHaveCountAsync(0);
        await Expect(detail.DeletedBadge).ToHaveCountAsync(0);
        await Expect(detail.Value).ToBeEnabledAsync();
        await Expect(detail.Value).ToHaveValueAsync(value);
        await Expect(detail.Version).ToHaveTextAsync("v1");

        await detail.CloseAsync();
        await Expect(keyValues.EmptyState(EmptyDeleted)).ToBeVisibleAsync();
        await Expect(Page.GetByText(EmptyDeletedDescription, new() { Exact = true })).ToBeVisibleAsync();

        await keyValues.ActiveButton.ClickAsync();
        await App.ExpectQueryAsync("state", null);
        await Expect(keyValues.Item(key)).ToBeVisibleAsync();
    }

    // KV-LIF-02: Remove keys for good with Delete permanently and Purge.
    // Delete permanently stays disabled until the exact key is typed and removes an active key at once; Purge removes
    // the selected deleted keys once the view's name is typed. Neither key exists afterwards, not even by its URL.
    [Test]
    [TestCase(165)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_DeletePermanentlyAndPurge_RemoveForGood()
    {
        var first = PlatformData.KeyValueKey();
        var second = PlatformData.KeyValueKey();
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(first, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, first).CloseAsync();
        await keyValues.CreateAsync(second, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, second).CloseAsync();

        await keyValues.RowActionAsync(first, "Delete permanently");
        var permanent = keyValues.DeletePermanentlyDialog;
        await permanent.ExpectOpenAsync();
        await Expect(permanent.Description).ToHaveTextAsync($"'{first}' and every version of it are removed for good; nothing can restore it.");
        await Expect(permanent.ConfirmationInput).ToHaveAccessibleNameAsync($"Type {first} to confirm");
        await Expect(permanent.Button("Delete permanently")).ToBeDisabledAsync();

        await permanent.TypeConfirmationAsync(first[..^1]);
        await Expect(permanent.Button("Delete permanently")).ToBeDisabledAsync();
        await permanent.TypeConfirmationAsync(first);
        await Expect(permanent.Button("Delete permanently")).ToBeEnabledAsync();

        await permanent.ConfirmAsync("Delete permanently");
        await Expect(App.Toast($"'{first}' removed for good")).ToBeVisibleAsync();
        await Expect(keyValues.Item(first)).ToHaveCountAsync(0);

        await keyValues.DeleteFromRowMenuAsync(second);

        await keyValues.DeletedButton.ClickAsync();
        await App.ExpectQueryAsync("state", "Deleted");
        await keyValues.SelectKey(second).CheckAsync();
        await Expect(keyValues.Item(first)).ToHaveCountAsync(0);
        await Expect(keyValues.BulkSelected(1)).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Restore")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Purge")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Clear selection")).ToBeVisibleAsync();

        await keyValues.BulkActionButton("Purge").ClickAsync();
        var purge = keyValues.PurgeDialog;
        await purge.ExpectOpenAsync();
        await Expect(purge.Description).ToHaveTextAsync("1 deleted key(s) and every version of them are removed for good, before their retention ends.");
        await Expect(purge.ConfirmationInput).ToHaveAccessibleNameAsync($"Type {project.View} to confirm");
        await purge.TypeConfirmationAsync(project.View);
        await purge.ConfirmAsync("Purge");
        await Expect(App.Toast("1 key(s) purged")).ToBeVisibleAsync();
        await Expect(keyValues.EmptyState(EmptyDeleted)).ToBeVisibleAsync();

        foreach (var key in new[] { first, second })
        {
            var detail = new KeyValueDetail(App, project, key);
            await detail.OpenAsync();
            await Expect(detail.NotFoundAlert).ToBeVisibleAsync();
            await Expect(detail.NotFoundDescription).ToHaveTextAsync($"The key '{key}' does not exist in view '{project.View}'.");
        }
    }

    // KV-LIF-03: Bulk delete and bulk restore of selected keys.
    // The toolbar appears with a selection and goes with Clear selection; Select all and Delete remove both keys after a
    // confirmation, and in the Deleted keys Select all and Restore bring both back without one.
    [Test]
    [TestCase(167)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_BulkDeleteAndRestore_ReturnsKeys()
    {
        var first = PlatformData.KeyValueKey();
        var second = PlatformData.KeyValueKey();
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(first, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, first).CloseAsync();
        await keyValues.CreateAsync(second, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, second).CloseAsync();

        await keyValues.SelectKey(first).CheckAsync();
        await keyValues.SelectKey(second).CheckAsync();
        await Expect(keyValues.BulkSelected(2)).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Delete")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Clear selection")).ToBeVisibleAsync();

        await keyValues.BulkActionButton("Clear selection").ClickAsync();
        await Expect(keyValues.BulkActions).ToBeHiddenAsync();
        await Expect(keyValues.SelectKey(first)).Not.ToBeCheckedAsync();
        await Expect(keyValues.SelectKey(second)).Not.ToBeCheckedAsync();

        // Select all is safe here only: the disposable project holds just the test's keys.
        await keyValues.SelectAll.CheckAsync();
        await Expect(keyValues.BulkSelected(2)).ToBeVisibleAsync();

        await keyValues.BulkActionButton("Delete").ClickAsync();
        var delete = keyValues.BulkDeleteDialog(2);
        await delete.ExpectOpenAsync();
        await Expect(delete.Description).ToHaveTextAsync("Deleted keys can be restored for 90 days, with every version; after that they are removed for good.");
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast("2 key(s) deleted")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActions).ToBeHiddenAsync();
        await Expect(keyValues.EmptyState(EmptyActive)).ToBeVisibleAsync();

        await keyValues.DeletedButton.ClickAsync();
        await App.ExpectQueryAsync("state", "Deleted");
        await keyValues.SelectAll.CheckAsync();
        await Expect(keyValues.BulkSelected(2)).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Restore")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Purge")).ToBeVisibleAsync();

        await keyValues.BulkActionButton("Restore").ClickAsync();
        await Expect(App.Toast("2 key(s) restored")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
        await Expect(keyValues.EmptyState(EmptyDeleted)).ToBeVisibleAsync();

        await keyValues.ActiveButton.ClickAsync();
        await App.ExpectQueryAsync("state", null);
        await Expect(keyValues.Item(first)).ToBeVisibleAsync();
        await Expect(keyValues.Item(second)).ToBeVisibleAsync();
    }

    // KV-LIF-04: New key over a deleted key continues its versions.
    // New key with the name of a deleted key is no conflict (it sends ExpectedVersion 0, which a deleted key matches):
    // it brings the key back as version 2 with the new value, while version 1 keeps the old value and an expiry, and
    // the Deleted keys are empty again.
    [Test]
    [TestCase(168)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_NewKeyOverDeleted_ContinuesVersions()
    {
        var key = PlatformData.KeyValueKey();
        var first = PlatformData.Slug("value");
        var second = PlatformData.Slug("value");
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, first);
        var detail = new KeyValueDetail(App, project, key);
        await detail.CloseAsync();

        await keyValues.DeleteFromRowMenuAsync(key);
        await Expect(ToastText("Restore it from the Deleted keys.")).ToBeVisibleAsync();
        await Expect(keyValues.EmptyState(EmptyActive)).ToBeVisibleAsync();

        await keyValues.CreateAsync(key, second);
        await Expect(detail.Region).ToBeVisibleAsync();
        await Expect(detail.LevelBadge).ToHaveTextAsync("Public");
        await Expect(detail.DeletedBadge).ToHaveCountAsync(0);
        await Expect(detail.Version).ToHaveTextAsync("v2");
        await Expect(detail.Value).ToHaveValueAsync(second);
        await Expect(detail.Value).ToBeEnabledAsync();

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.VersionCells).ToHaveTextAsync(["v2(current)", "v1"]);
        await Expect(versions.Cell(2, "Level")).ToHaveTextAsync("Public");
        await Expect(versions.Cell(2, "Expires")).ToHaveTextAsync("—");
        await Expect(versions.Cell(1, "Level")).ToHaveTextAsync("Public");
        await Expect(versions.Cell(1, "Expires")).ToHaveTextAsync(Digit());

        await versions.ViewButton(1).ClickAsync();
        await Expect(versions.VersionCard(1)).ToBeVisibleAsync();
        await Expect(versions.VersionValue(1)).ToHaveTextAsync(first);

        await detail.CloseAsync();
        await Expect(keyValues.ItemCell(key, "Version")).ToHaveTextAsync("v2");

        await keyValues.DeletedButton.ClickAsync();
        await Expect(keyValues.EmptyState(EmptyDeleted)).ToBeVisibleAsync();
        await Expect(Page.GetByText(EmptyDeletedDescription, new() { Exact = true })).ToBeVisibleAsync();
    }

    // A description of a toast (the newest of that text).
    private ILocator ToastText(string text)
    {
        return App.Notifications.GetByText(text, new() { Exact = true }).Last;
    }

    // A date in the Expires column of an older version.
    [GeneratedRegex(@"\d")]
    private static partial Regex Digit();
}
