using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// List: the values store's list, in a disposable project, because the list loads only the first page of a view (1,000
// keys on a live target, by key) and a shared view grows with every run. The search over keys and public values in the
// browser, the level filter on the server and the query the detail keeps, the row menu (Open, Copy key, Copy binding,
// Reveal), and the context switcher dropping the key, the selection and the secrets opt-in of Export.
[Area(Area.KeyValues)]
public sealed class KeyValueListTests : PlatformTest
{
    // KV-LST-01: Search, level filter and the query kept by the detail.
    // The search narrows the list to keys and public values that contain the text (never a secret value, so the
    // secret's value finds nothing and the empty state offers no New key); the level filter lists only the keys of a
    // level and keeps its API name in the URL; a detail opened from a search keeps ?q in its tabs and when it closes.
    [Test]
    [TestCase(129)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task KeyValues_SearchAndLevelFilter_NarrowTheList()
    {
        var publicKey = PlatformData.KeyValueKey();
        var secretKey = PlatformData.KeyValueKey();
        var publicValue = PlatformData.Slug("value");
        var secretValue = PlatformData.Slug("secret");
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(publicKey, publicValue);
        var detail = new KeyValueDetail(App, project, publicKey);
        await detail.CloseAsync();
        await keyValues.CreateSecretAsync(secretKey, secretValue, "Standard");
        await new KeyValueDetail(App, project, secretKey).CloseAsync();
        await Expect(keyValues.ListCounter).ToHaveTextAsync("2 of 2");

        await keyValues.SearchAsync(publicKey);
        await Expect(keyValues.Item(publicKey)).ToBeVisibleAsync();
        await Expect(keyValues.Item(secretKey)).ToHaveCountAsync(0);
        await Expect(keyValues.ListCounter).ToHaveTextAsync("1 of 2");

        await keyValues.SearchAsync(publicValue);
        await Expect(keyValues.Item(publicKey)).ToBeVisibleAsync();
        await Expect(keyValues.Item(secretKey)).ToHaveCountAsync(0);
        await Expect(keyValues.ListCounter).ToHaveTextAsync("1 of 2");

        await keyValues.SearchAsync(secretValue);
        await Expect(keyValues.EmptyState("No key matches the search")).ToBeVisibleAsync();
        await Expect(keyValues.EmptyStateBlock("No key matches the search").GetByRole(AriaRole.Button, new() { Name = "New key", Exact = true })).ToHaveCountAsync(0);
        await Expect(keyValues.ListCounter).ToHaveTextAsync("0 of 2");

        await keyValues.SearchAsync(string.Empty);
        await keyValues.FilterLevelAsync("Standard");
        await Expect(keyValues.Item(secretKey)).ToBeVisibleAsync();
        await Expect(keyValues.Item(publicKey)).ToHaveCountAsync(0);
        await Expect(keyValues.ListCounter).ToHaveTextAsync("1 of 1");

        await keyValues.FilterLevelAsync("All levels");
        await Expect(keyValues.Item(publicKey)).ToBeVisibleAsync();
        await Expect(keyValues.Item(secretKey)).ToBeVisibleAsync();
        await Expect(keyValues.ListCounter).ToHaveTextAsync("2 of 2");

        await keyValues.SearchAsync(publicKey);
        await keyValues.OpenItemAsync(publicKey);
        await App.ExpectQueryAsync("q", publicKey);
        await Expect(detail.Region).ToBeVisibleAsync();

        await detail.OpenVersionsAsync();
        await App.ExpectQueryAsync("q", publicKey);

        await detail.CloseAsync();
        await App.ExpectQueryAsync("q", publicKey);
        await Expect(keyValues.Search).ToHaveValueAsync(publicKey);
        await Expect(detail.Region).ToBeHiddenAsync();
    }

    // KV-LST-02: The row menu copies the key and the binding and opens the detail.
    // A public key's menu offers Open, the two copies and the deletes, without Reveal; the copies toast and put the key
    // and its binding "@<key>" into the clipboard, and Open opens the detail. A secret's menu has Reveal, which only
    // opens the detail: the value stays masked.
    [Test]
    [TestCase(131)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_RowMenu_CopiesAndOpens()
    {
        var key = PlatformData.KeyValueKey();
        var secretKey = PlatformData.KeyValueKey();
        await InstallClipboardRecorderAsync();
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, PlatformData.Slug("value"));
        var detail = new KeyValueDetail(App, project, key);
        await detail.CloseAsync();
        await keyValues.CreateSecretAsync(secretKey, PlatformData.Slug("secret"), "Standard");
        var secretDetail = new KeyValueDetail(App, project, secretKey);
        await secretDetail.CloseAsync();

        var items = await keyValues.OpenRowMenuAsync(key);
        await Expect(items).ToHaveTextAsync(["Open", "Copy key", "Copy binding", "Delete", "Delete permanently"]);
        await Expect(keyValues.RowMenuItem("Reveal")).ToHaveCountAsync(0);

        await keyValues.RowMenuItem("Copy key").ClickAsync();
        await Expect(App.Toast("Key copied")).ToBeVisibleAsync();
        await WaitForCopiedTextAsync(detail.Key);

        await keyValues.RowActionAsync(key, "Copy binding");
        await Expect(App.Toast($"Binding @{detail.Key} copied")).ToBeVisibleAsync();
        await WaitForCopiedTextAsync($"@{detail.Key}");

        await keyValues.RowActionAsync(key, "Open");
        await App.ExpectAtAsync(project.KeyValue(key));
        await Expect(detail.Region).ToBeVisibleAsync();

        await detail.CloseAsync();
        await keyValues.OpenRowMenuAsync(secretKey);
        await Expect(keyValues.RowMenuItem("Reveal")).ToBeVisibleAsync();

        await keyValues.RowMenuItem("Reveal").ClickAsync();
        await App.ExpectAtAsync(project.KeyValue(secretKey));
        await Expect(secretDetail.Region).ToBeVisibleAsync();
        await Expect(secretDetail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(secretDetail.RevealButton).ToBeVisibleAsync();
        await Expect(secretDetail.HideButton).ToHaveCountAsync(0);
    }

    // KV-LST-03: Switching the project drops the key, the selection and the secrets opt-in.
    // With a key selected, "Include secret values" checked and a detail open, a switch to the target's project lands
    // on its default view's values store without the key, a query, the detail or the selection, and Export's opt-in is
    // unchecked; back in the disposable project the key is listed and not selected.
    [Test]
    [TestCase(134)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_SwitchProject_DropsKeySelectionAndOptIn()
    {
        var key = PlatformData.KeyValueKey();
        var project = await CreateDisposableProjectAsync();
        var target = AppRoutes.For(Target) with { View = AppRoutes.DefaultView };

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, PlatformData.Slug("value"));
        var detail = new KeyValueDetail(App, project, key);
        await detail.CloseAsync();

        await keyValues.SelectKey(key).CheckAsync();
        await Expect(keyValues.BulkSelected(1)).ToBeVisibleAsync();

        var export = new KeyValueExportMenu(App);
        await export.OpenAsync();
        await export.IncludeSecretValues.ClickAsync();
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(export.Menu).ToBeVisibleAsync();

        await export.CloseAsync();
        await keyValues.OpenItemAsync(key);
        await Expect(detail.Region).ToBeVisibleAsync();
        await Expect(SelectedText(1)).ToBeVisibleAsync();

        // While the switcher's menu closes the page is aria-hidden: the detail and the selection are found without
        // their roles.
        await new ContextSwitcher(App, project).PickProjectAsync(target.Project);
        await App.ExpectAtAsync(target.KeyValues);
        await ExpectNoListQueryAsync();
        await Expect(detail.Panel.Root).ToHaveCountAsync(0);
        await Expect(SelectedText(1)).ToHaveCountAsync(0);

        await export.OpenAsync();
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "false");
        await export.CloseAsync();

        await new ContextSwitcher(App, target).PickProjectAsync(project.Project);
        await App.ExpectAtAsync(project.KeyValues);
        await Expect(keyValues.Item(key)).ToBeVisibleAsync();
        await Expect(keyValues.SelectKey(key)).Not.ToBeCheckedAsync();
    }

    // "<n> selected" of the Bulk actions toolbar, by its text only.
    private ILocator SelectedText(int count)
    {
        return Page.GetByText($"{count} selected", new() { Exact = true });
    }

    // The URL has no query of the list (q, level, state) or any other, but the mock's scenario.
    private async Task ExpectNoListQueryAsync()
    {
        try
        {
            await Page.WaitForFunctionAsync(
                "() => [...new URLSearchParams(location.search).keys()].every(name => name === 'mock-scenario')",
                null,
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"The URL kept a query after the switch: {Page.Url}.", exception);
        }
    }
}
