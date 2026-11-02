using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Browser Settings: the cards of Settings that this browser keeps per project (localStorage '2s-admin:project-settings',
// entry '<organization>.<project>'): the default time zone of new annotations, the project's date format, and the
// names, plurals, colors and icons of the seven annotation types with their resets. No Save and no toast; every test
// has a fresh browser context, so they start from the defaults and change nothing project-wide.
[Area(Area.Settings)]
public sealed partial class SettingsBrowserTests : PlatformTest
{
    // The light colors of the types (every profile uses the light scheme): the built-in Execution (emerald) and Unit
    // (fuchsia), and the picked Red and Gray.
    private const string Emerald = "#047857";
    private const string Fuchsia = "#a21caf";
    private const string Red = "#b91c1c";
    private const string Gray = "#52525b";

    // The browser runs in Europe/Prague; a zone with another offset shows that the setting, not the browser, decides.
    private const string Tokyo = "Asia/Tokyo";

    // SET-BRW-01: The default time zone is kept in this browser, and new annotations start and are saved in it.
    // The project's default time zone starts as UTC; Asia/Tokyo, picked through the search, is kept after a reload under
    // the project's entry of localStorage. New annotation starts in it, and the subject it creates is saved in it: its
    // detail shows Asia/Tokyo, not the browser's zone (the former ANN-NEW-03).
    [Test]
    [TestCase(65)]
    [UserRole(Role.Editor)]
    public async Task Settings_DefaultTimeZone_NewAnnotationStartsInIt()
    {
        var subject = PlatformData.AnnotationKey(PlatformData.Subject);

        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        var zone = settings.DefaultTimeZone;
        await Expect(zone.Button).ToHaveTextAsync("UTC");
        await Expect(settings.DefaultTimeZoneHelp).ToBeVisibleAsync();

        await zone.Button.ClickAsync();
        await zone.Search.FillAsync("Tokyo");
        await Expect(zone.Option(Tokyo)).ToHaveAccessibleNameAsync(TokyoOption());
        await zone.Option(Tokyo).ClickAsync();
        await Expect(zone.Button).ToContainTextAsync(Tokyo);
        await Expect(App.ShownToasts).ToHaveCountAsync(0);

        await settings.OpenAsync();
        await Expect(zone.Button).ToContainTextAsync(Tokyo);
        await settings.ExpectStoredAsync(Tokyo, "defaultTimeZone");

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var dialog = await annotations.OpenNewAnnotationAsync();
        await Expect(dialog.TimeZone.Button).ToContainTextAsync(Tokyo);
        await Expect(dialog.TimeZone.Button).ToHaveAccessibleDescriptionAsync("Default from the project settings.");

        await dialog.CreateAsync(subject);
        await App.ExpectAtAsync(Routes.Annotation(subject));

        var detail = new AnnotationDetail(App, Routes);
        await Expect(detail.FormTimeZone.Button).ToContainTextAsync(Tokyo);
        await Expect(detail.FormTimeZone.Button).ToHaveAccessibleDescriptionAsync("Dates are entered and shown in this zone.");
    }

    // SET-BRW-02: The project's date format changes how dates are shown.
    // A new key value's "Updated" reads yyyy-mm-dd at first; the project's date format dd.mm.yyyy (its item names
    // today's date in the format) shows the same instant as dd.mm.yyyy in the key's detail, and stays after a reload.
    [Test]
    [TestCase(78)]
    [UserRole(Role.Editor)]
    public async Task Settings_DateFormat_ChangesShownDates()
    {
        var key = PlatformData.KeyValueKey();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, PlatformData.Slug("value"));
        var updated = keyValues.DetailValue(key, "Updated");
        await Expect(updated).ToHaveTextAsync(IsoDateTime());
        var shown = await updated.InnerTextAsync();

        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.IsoFormat);
        await Expect(settings.DateFormatHelp).ToBeVisibleAsync();

        await settings.DateFormat.SelectAsync(DateFormatSelect.DottedFormat);
        await Expect(App.ShownToasts).ToHaveCountAsync(0);

        await App.GotoAsync(Routes.KeyValue(key));
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.ReformatIsoDateTime(shown, DateFormatSelect.DottedFormat));

        await settings.OpenAsync();
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.DottedFormat);
    }

    // SET-BRW-03: The project renames, recolors and re-icons an annotation type.
    // Execution renamed to Job (its plural's placeholder becomes Jobs), colored red and given the rocket icon: the
    // preview and the CSS variable --st-exe follow at once, the icon is kept in localStorage, everything stays after a
    // reload, and Modeling's Templates shows Job instead of Execution.
    [Test]
    [TestCase(88)]
    [UserRole(Role.Editor)]
    public async Task Settings_CustomizeType_ShowsOnTemplates()
    {
        const string execution = PlatformData.Execution;

        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        var types = settings.ProjectTypes;
        await Expect(types.Preview(execution)).ToHaveTextAsync("Execution");
        await App.ExpectTypeColorAsync(execution, Emerald);

        await types.SetNameAsync(execution, "Job");
        await Expect(types.Plural(execution)).ToHaveAttributeAsync("placeholder", "Jobs");
        await Expect(types.Reset(execution)).ToBeVisibleAsync();

        await types.PickColorAsync(execution, "Red");
        await App.ExpectTypeColorAsync(execution, Red);

        await types.PickIconAsync(execution, "Rocket");
        await settings.ExpectStoredAsync("i-lucide-rocket", "types", execution, "icon");

        await settings.OpenAsync();
        await Expect(types.Name(execution)).ToHaveValueAsync("Job");
        await Expect(types.Preview(execution)).ToHaveTextAsync("Job");
        await App.ExpectTypeColorAsync(execution, Red);

        var templates = new ModelingTemplatePage(App, Routes);
        await templates.OpenListAsync();
        await Expect(templates.ListRow(execution).GetByText("Job", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(App.Page.GetByText("Execution", new() { Exact = true })).ToHaveCountAsync(0);
    }

    // SET-BRW-04: A type's plural needs a name, and one type or all types can be reset.
    // The plural is disabled until the type has a name of its own (then its placeholder is the name's plural); a name
    // keeps at most 40 characters; "Reset <Type>" takes back one type and "Reset to built-in" all of them, with the
    // built-in names and colors.
    [Test]
    [TestCase(108)]
    [UserRole(Role.Editor)]
    public async Task Settings_ResetTypes_RestoresBuiltInNames()
    {
        const string subject = PlatformData.Subject;
        const string unit = PlatformData.Unit;

        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
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
        await App.ExpectTypeColorAsync(unit, Gray);
        await Expect(types.Reset(unit)).ToBeVisibleAsync();

        // Typed key by key, so the field's maxlength applies as it does for a person.
        var longName = PlatformData.UnspacedName(TypeAppearanceEditor.MaxNameLength + 5);
        var keptName = longName[..TypeAppearanceEditor.MaxNameLength];
        await types.Name(unit).PressSequentiallyAsync(longName);
        await types.Name(unit).PressAsync("Tab");
        await Expect(types.Name(unit)).ToHaveValueAsync(keptName);
        await Expect(types.Preview(unit)).ToHaveTextAsync(keptName);

        await types.Reset(subject).ClickAsync();
        await Expect(types.Preview(subject)).ToHaveTextAsync("Subject");
        await Expect(types.Plural(subject)).ToHaveValueAsync(string.Empty);
        await Expect(types.Plural(subject)).ToBeDisabledAsync();
        await Expect(types.Reset(unit)).ToBeVisibleAsync();

        await settings.ResetToBuiltInButton.ClickAsync();
        foreach (var code in TypeAppearanceEditor.Codes)
        {
            await Expect(types.Preview(code)).ToHaveTextAsync(TypeAppearanceEditor.BuiltInName(code));
        }

        await App.ExpectTypeColorAsync(unit, Fuchsia);
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();
    }

    // "Asia/Tokyo GMT+9": the zone and its offset (Japan has no daylight saving time).
    [GeneratedRegex(@"^Asia\/Tokyo\s*GMT\+9$")]
    private static partial Regex TokyoOption();

    // "2026-11-02 10:15 GMT+1": the date as yyyy-mm-dd, the 24-hour time and the zone.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \S+$")]
    private static partial Regex IsoDateTime();
}
