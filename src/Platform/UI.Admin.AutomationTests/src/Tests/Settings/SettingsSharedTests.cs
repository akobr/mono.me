using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Shared Settings: the project settings of Settings that the API keeps for everyone in the project (GET and PUT
// v1/{organization}/{project}/settings): the default time zone of new annotations, the project's date format, and the
// names, plurals, colors and icons of the seven annotation types with their resets. An Administrator or Owner changes
// them in one draft and saves it with "Save project settings" (the toast "Project settings saved"); the previews of the
// page show the draft, the rest of the app the saved settings. A saved setting stays for every later test and person,
// so each test saves them as an Owner in a disposable project and checks them after a reload; other tests rely on the
// target project's defaults (UTC, yyyy-mm-dd, the built-in type names).
[Area(Area.Settings)]
public sealed partial class SettingsSharedTests : PlatformTest
{
    // The light colors of the types (every profile uses the light scheme): the built-in Execution (emerald) and Unit
    // (fuchsia), and the picked Red and Gray.
    private const string Emerald = "#047857";
    private const string Fuchsia = "#a21caf";
    private const string Red = "#b91c1c";
    private const string Gray = "#52525b";

    // The browser runs in Europe/Prague; a zone with another offset shows that the setting, not the browser, decides.
    private const string Tokyo = "Asia/Tokyo";

    // SET-BRW-01: The project's default time zone is saved for everyone, and new annotations start and are saved in it.
    // In a disposable project the default time zone starts as UTC; Asia/Tokyo, picked through the search, changes the
    // draft only ("Unsaved changes", no toast) until "Save project settings", and is there after a reload. New annotation
    // starts in it, and the subject it creates is saved in it: its detail shows Asia/Tokyo, not the browser's zone (the
    // former ANN-NEW-03).
    [Test]
    [TestCase(65)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Settings_DefaultTimeZone_NewAnnotationStartsInIt()
    {
        var subject = PlatformData.AnnotationKey(PlatformData.Subject);
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await settings.WaitForEditableSettingsAsync();
        var zone = settings.DefaultTimeZone;
        await Expect(zone.Button).ToHaveTextAsync("UTC");
        await Expect(settings.DefaultTimeZoneHelp).ToBeVisibleAsync();
        await Expect(settings.SaveSettingsButton).ToBeDisabledAsync();

        await zone.Button.ClickAsync();
        await zone.Search.FillAsync("Tokyo");
        await Expect(zone.Option(Tokyo)).ToHaveAccessibleNameAsync(TokyoOption());
        await zone.Option(Tokyo).ClickAsync();
        await Expect(zone.Button).ToContainTextAsync(Tokyo);
        await Expect(settings.UnsavedSettingsNote).ToBeVisibleAsync();
        await Expect(App.ShownToasts).ToHaveCountAsync(0);

        await settings.SaveSettingsAsync();
        await Expect(settings.UnsavedSettingsNote).ToHaveCountAsync(0);

        await settings.ReloadAsync();
        await settings.WaitForEditableSettingsAsync();
        await Expect(zone.Button).ToContainTextAsync(Tokyo);
        await Expect(settings.SaveSettingsButton).ToBeDisabledAsync();

        var annotations = new AnnotationsPage(App, project);
        await annotations.OpenAsync();
        var dialog = await annotations.OpenNewAnnotationAsync();
        await Expect(dialog.TimeZone.Button).ToContainTextAsync(Tokyo);
        await Expect(dialog.TimeZone.Button).ToHaveAccessibleDescriptionAsync("Default from the project settings.");

        await dialog.CreateAsync(subject);
        await App.ExpectAtAsync(project.Annotation(subject));

        var detail = new AnnotationDetail(App, project);
        await Expect(detail.FormTimeZone.Button).ToContainTextAsync(Tokyo);
        await Expect(detail.FormTimeZone.Button).ToHaveAccessibleDescriptionAsync("Dates are entered and shown in this zone.");
    }

    // SET-BRW-02: The project's date format changes how dates are shown.
    // In a disposable project a new key value's "Updated" reads yyyy-mm-dd at first; the project's date format
    // dd.mm.yyyy (its item names today's date in the format), saved with "Save project settings", shows the same instant
    // as dd.mm.yyyy in the key's detail, and is there after a reload.
    [Test]
    [TestCase(78)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Settings_DateFormat_ChangesShownDates()
    {
        var key = PlatformData.KeyValueKey();
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, PlatformData.Slug("value"));
        var updated = keyValues.DetailValue(key, "Updated");
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.IsoDateTime());
        var shown = await updated.InnerTextAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await settings.WaitForEditableSettingsAsync();
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.IsoFormat);
        await Expect(settings.DateFormatHelp).ToBeVisibleAsync();

        await settings.DateFormat.SelectAsync(DateFormatSelect.DottedFormat);
        await Expect(App.ShownToasts).ToHaveCountAsync(0);
        await settings.SaveSettingsAsync();

        await App.GotoAsync(project.KeyValue(key));
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.ReformatIsoDateTime(shown, DateFormatSelect.DottedFormat));

        await settings.OpenAsync();
        await settings.WaitForEditableSettingsAsync();
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.DottedFormat);
        await Expect(settings.SaveSettingsButton).ToBeDisabledAsync();
    }

    // SET-BRW-03: The project renames, recolors and re-icons an annotation type for everyone.
    // In a disposable project Execution is renamed to Job (its plural's placeholder becomes Jobs), colored red and given
    // the rocket icon: the preview follows at once, the CSS variable --st-exe once "Save project settings" saved the
    // draft; everything is there after a reload, and Modeling's Templates shows Job instead of Execution.
    [Test]
    [TestCase(88)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Settings_CustomizeType_ShowsOnTemplates()
    {
        const string execution = PlatformData.Execution;
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await settings.WaitForEditableSettingsAsync();
        var types = settings.ProjectTypes;
        await Expect(types.Preview(execution)).ToHaveTextAsync("Execution");
        await App.ExpectTypeColorAsync(execution, Emerald);

        await types.SetNameAsync(execution, "Job");
        await Expect(types.Plural(execution)).ToHaveAttributeAsync("placeholder", "Jobs");
        await Expect(types.Reset(execution)).ToBeVisibleAsync();

        await types.PickColorAsync(execution, "Red");
        await types.PickIconAsync(execution, "Rocket");
        await Expect(settings.UnsavedSettingsNote).ToBeVisibleAsync();

        // The rest of the app keeps the saved settings until Save.
        await App.ExpectTypeColorAsync(execution, Emerald);
        await settings.SaveSettingsAsync();
        await App.ExpectTypeColorAsync(execution, Red);

        await settings.ReloadAsync();
        await settings.WaitForEditableSettingsAsync();
        await Expect(types.Name(execution)).ToHaveValueAsync("Job");
        await Expect(types.Preview(execution)).ToHaveTextAsync("Job");
        await Expect(types.Icon(execution)).ToHaveTextAsync("Rocket");
        await App.ExpectTypeColorAsync(execution, Red);

        var templates = new ModelingTemplatePage(App, project);
        await templates.OpenListAsync();
        await Expect(templates.ListRow(execution).GetByText("Job", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(App.Page.GetByText("Execution", new() { Exact = true })).ToHaveCountAsync(0);
    }

    // SET-BRW-04: A type's plural needs a name, and one type or all types can be reset.
    // In a disposable project: the plural is disabled until the type has a name of its own (then its placeholder is the
    // name's plural); a name keeps at most 40 characters; the saved name, plural and color are there after a reload.
    // "Reset <Type>" takes back one type and "Reset to built-in" all of them in the draft; once saved, the built-in names
    // and colors are back, also after a reload.
    [Test]
    [TestCase(108)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Settings_ResetTypes_RestoresBuiltInNames()
    {
        const string subject = PlatformData.Subject;
        const string unit = PlatformData.Unit;
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await settings.WaitForEditableSettingsAsync();
        var types = settings.ProjectTypes;
        await Expect(types.Plural(subject)).ToBeDisabledAsync();
        await Expect(types.Plural(subject)).ToHaveAttributeAsync("title", "Set a name first");
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();

        await types.SetNameAsync(subject, "Batch");
        await Expect(types.Plural(subject)).ToBeEnabledAsync();
        await Expect(types.Plural(subject)).ToHaveAttributeAsync("placeholder", "Batches");
        await Expect(types.Reset(subject)).ToBeVisibleAsync();
        await Expect(settings.ResetToBuiltInButton).ToBeEnabledAsync();

        await types.SetPluralAsync(subject, "Batch runs");
        await types.PickColorAsync(unit, "Gray");
        await Expect(types.Preview(subject)).ToHaveTextAsync("Batch");
        await Expect(types.Reset(unit)).ToBeVisibleAsync();

        // Typed key by key, so the field's maxlength applies as it does for a person.
        var longName = PlatformData.UnspacedName(TypeAppearanceEditor.MaxNameLength + 5);
        var keptName = longName[..TypeAppearanceEditor.MaxNameLength];
        await types.Name(unit).PressSequentiallyAsync(longName);
        await types.Name(unit).PressAsync("Tab");
        await Expect(types.Name(unit)).ToHaveValueAsync(keptName);
        await Expect(types.Preview(unit)).ToHaveTextAsync(keptName);

        await settings.SaveSettingsAsync();
        await App.ExpectTypeColorAsync(unit, Gray);

        await settings.ReloadAsync();
        await settings.WaitForEditableSettingsAsync();
        await Expect(types.Name(subject)).ToHaveValueAsync("Batch");
        await Expect(types.Plural(subject)).ToHaveValueAsync("Batch runs");
        await Expect(types.Name(unit)).ToHaveValueAsync(keptName);
        await App.ExpectTypeColorAsync(unit, Gray);

        await types.Reset(subject).ClickAsync();
        await Expect(types.Preview(subject)).ToHaveTextAsync("Subject");
        await Expect(types.Plural(subject)).ToHaveValueAsync(string.Empty);
        await Expect(types.Plural(subject)).ToBeDisabledAsync();
        await Expect(types.Reset(unit)).ToBeVisibleAsync();

        await settings.ResetToBuiltInButton.ClickAsync();
        await ExpectBuiltInNamesAsync(types);
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();

        await settings.SaveSettingsAsync();
        await App.ExpectTypeColorAsync(unit, Fuchsia);

        await settings.ReloadAsync();
        await settings.WaitForEditableSettingsAsync();
        await ExpectBuiltInNamesAsync(types);
        await Expect(types.Name(unit)).ToHaveValueAsync(string.Empty);
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();
        await Expect(settings.SaveSettingsButton).ToBeDisabledAsync();
        await App.ExpectTypeColorAsync(unit, Fuchsia);
    }

    // Every preview shows the built-in name of its type.
    private static async Task ExpectBuiltInNamesAsync(TypeAppearanceEditor types)
    {
        foreach (var code in TypeAppearanceEditor.Codes)
        {
            await Expect(types.Preview(code)).ToHaveTextAsync(TypeAppearanceEditor.BuiltInName(code));
        }
    }

    // "Asia/Tokyo GMT+9": the zone and its offset (Japan has no daylight saving time).
    [GeneratedRegex(@"^Asia\/Tokyo\s*GMT\+9$")]
    private static partial Regex TokyoOption();
}
