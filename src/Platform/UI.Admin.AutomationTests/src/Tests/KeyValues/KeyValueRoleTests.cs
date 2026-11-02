using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static _42.Platform.UI.Admin.AutomationTests.Support.KeyValueExportFiles;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Roles: what a Reader and a Contributor get in the values store, each in the project of Target:RoleProjects for its
// role (on the mock northwind/portal and acme/shipping, where the signed-in user holds exactly that role; the UI
// follows the project's role). A Reader reads and exports without the secret values; a Contributor writes public keys
// only, without secret levels and without the secret values of Export.
[Area(Area.KeyValues)]
public sealed class KeyValueRoleTests : PlatformTest
{
    // KV-ROL-01: A Reader sees Values store read-only and can export it.
    // A Reader has the search and Export but neither Import nor New key; Export offers the three formats without
    // "Include secret values", and the key/value document carries no secret's value. The Reader's view is not known on
    // a live target, so only the controls and the document's format are asserted.
    [Test]
    [TestCase(212)]
    [UserRole(Role.Reader)]
    public async Task KeyValues_Reader_SeesReadOnlyStore()
    {
        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await Expect(keyValues.Search).ToBeVisibleAsync();
        await Expect(keyValues.ExportButton).ToBeVisibleAsync();
        await Expect(keyValues.ImportButton).ToHaveCountAsync(0);
        await Expect(keyValues.NewKeyButton).ToHaveCountAsync(0);

        var export = new KeyValueExportMenu(App);
        await export.OpenAsync();
        await Expect(export.FormatItems).ToHaveTextAsync(KeyValueExportMenu.Formats);
        await Expect(export.IncludeSecretValues).ToHaveCountAsync(0);

        var file = await DownloadAsync(() => export.Format(KeyValueExportMenu.Document).ClickAsync(), 30_000);
        ExpectFileName(file, $"{Routes.Organization}-{Routes.Project}-{Routes.View}.key-values.json");
        ExpectNoSecretValues(DocumentItems(file));
    }

    // KV-ROL-02: A Contributor creates and edits only public keys.
    // A Contributor's New key offers only Public, also with Secret on; the new key's value is editable and saved as a
    // new version, but there is no Set new value. Export has no "Include secret values", and Import offers no Mark and
    // only Public per row; Cancel imports nothing.
    [Test]
    [TestCase(213)]
    [UserRole(Role.Contributor)]
    public async Task KeyValues_Contributor_CreatesPublicKeysOnly()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        var newValue = PlatformData.Slug("value");
        var prefix = PlatformData.KeyValuePrefix();
        var password = $"{prefix}.password";

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await Expect(keyValues.ExportButton).ToBeVisibleAsync();
        await Expect(keyValues.ImportButton).ToBeVisibleAsync();
        await Expect(keyValues.NewKeyButton).ToBeVisibleAsync();

        await keyValues.OpenNewKeyAsync();
        await keyValues.NewKeyLevel.ClickAsync();
        await ExpectOnlyPublicAsync(keyValues);

        await keyValues.NewKeySecret.SetCheckedAsync(true);
        await Expect(keyValues.NewKeyLevel).ToHaveTextAsync("Public");

        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(value);
        await keyValues.CreateButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.KeyValue(key));

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.Value).ToBeEditableAsync();
        await Expect(detail.SaveButton).ToBeDisabledAsync();
        await Expect(detail.SetNewValueButton).ToHaveCountAsync(0);

        await detail.Value.FillAsync(newValue);
        await detail.SaveAsync(2);

        // On a phone the detail covers the navbar's actions.
        await detail.CloseAsync();
        var export = new KeyValueExportMenu(App);
        await export.OpenAsync();
        await Expect(export.FormatItems).ToHaveTextAsync(KeyValueExportMenu.Formats);
        await Expect(export.IncludeSecretValues).ToHaveCountAsync(0);
        await export.CloseAsync();

        var import = new KeyValueImportDialog(App);
        await import.OpenAsync();
        await import.PasteAndReviewAsync($"{PlatformData.ToEnvKey(prefix)}__PASSWORD=x");
        await Expect(import.MarkPattern).ToHaveCountAsync(0);
        await import.Level(password).ClickAsync();
        await ExpectOnlyPublicAsync(keyValues);

        await import.BackAsync();
        await import.CancelAsync();
        var imported = new KeyValueDetail(App, Routes, password);
        await imported.OpenAsync();
        await Expect(imported.NotFoundAlert).ToBeVisibleAsync();
    }

    // The open level select offers Public only; picking it closes the list without a change (Escape could close the
    // dialog around it).
    private static async Task ExpectOnlyPublicAsync(KeyValuesPage keyValues)
    {
        await Expect(keyValues.SelectOption("Public")).ToBeVisibleAsync();
        await Expect(keyValues.SelectOptions).ToHaveCountAsync(1);
        await keyValues.SelectOption("Public").ClickAsync();
        await Expect(keyValues.SelectOptions).ToHaveCountAsync(0);
    }
}
