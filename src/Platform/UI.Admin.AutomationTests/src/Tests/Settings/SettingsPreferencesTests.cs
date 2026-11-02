using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Preferences: the account page's Preferences card (row density, document format, annotation editing, the personal
// date format, "Reset preferences") and its personal Annotation types ("Reset types"), kept in this browser
// (localStorage '2s-admin:preferences'). A personal choice wins over the project's, and the project's over the
// built-in one.
[Area(Area.Settings)]
public sealed partial class SettingsPreferencesTests : PlatformTest
{
    // The light color of the project's red Execution (every profile uses the light scheme).
    private const string Red = "#b91c1c";

    // SET-PRF-01: Preferences are kept in this browser and 'Reset preferences' restores the defaults.
    // From the defaults of a fresh browser (Compact, JSON, Form, Project default), the changed tabs, the personal date
    // format d mmm yyyy and a personal Execution name stay after a reload, as localStorage holds them; "Reset
    // preferences" brings back every default, the personal types too.
    [Test]
    [TestCase(118)]
    public async Task Preferences_ChangeAndReset_RestoresDefaults()
    {
        const string execution = PlatformData.Execution;

        var account = new AccountPage(App, Routes);
        await account.OpenPreferencesAsync();
        await ExpectDefaultPreferencesAsync(account);

        await account.SelectPreferenceTabAsync(account.RowDensityTab("Comfortable"));
        await account.SelectPreferenceTabAsync(account.DocumentFormatTab("YAML"));
        await account.SelectPreferenceTabAsync(account.AnnotationEditingTab("YAML"));
        await account.PreferredDateFormat.SelectAsync(DateFormatSelect.MonthNameFormat);

        var types = account.PersonalTypes;
        await types.SetNameAsync(execution, "Run");
        await Expect(account.ResetTypesButton).ToBeEnabledAsync();

        await account.OpenPreferencesAsync();
        await Expect(account.RowDensityTab("Comfortable")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(account.DocumentFormatTab("YAML")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(account.AnnotationEditingTab("YAML")).ToHaveAttributeAsync("aria-selected", "true");
        await account.PreferredDateFormat.ExpectFormatAsync(DateFormatSelect.MonthNameFormat);
        await Expect(types.Name(execution)).ToHaveValueAsync("Run");
        await Expect(types.Preview(execution)).ToHaveTextAsync("Run");
        await account.ExpectStoredPreferenceAsync("comfortable", "density");
        await account.ExpectStoredPreferenceAsync("yaml", "editorFormat");
        await account.ExpectStoredPreferenceAsync("yaml", "annotationMode");
        await account.ExpectStoredPreferenceAsync(DateFormatSelect.MonthNameFormat, "dateFormat");
        await account.ExpectStoredPreferenceAsync("Run", "types", execution, "label");

        await account.ResetPreferencesButton.ClickAsync();
        await ExpectDefaultPreferencesAsync(account);
        await Expect(types.Name(execution)).ToHaveValueAsync(string.Empty);
    }

    // SET-PRF-02: A personal date format in Preferences wins over the project's.
    // With the project's dd.mm.yyyy, a key value's "Updated" reads dd.mm.yyyy; the personal mm/dd/yyyy shows the same
    // instant as mm/dd/yyyy and Settings say so (the note with "Open Preferences"), while the project's format stays;
    // back to "Project default", the note is gone and dd.mm.yyyy applies again.
    [Test]
    [TestCase(124)]
    [UserRole(Role.Editor)]
    public async Task Preferences_PersonalDateFormat_WinsOverProject()
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
        await settings.DateFormat.SelectAsync(DateFormatSelect.DottedFormat);
        await App.GotoAsync(Routes.KeyValue(key));
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.ReformatIsoDateTime(shown, DateFormatSelect.DottedFormat));

        var account = new AccountPage(App, Routes);
        await account.OpenPreferencesAsync();
        await Expect(account.PreferredDateFormat.Combobox).ToHaveTextAsync(DateFormatSelect.ProjectDefault);
        await account.PreferredDateFormat.SelectAsync(DateFormatSelect.UsFormat);

        await settings.OpenAsync();
        await Expect(settings.PersonalDateFormatNote(DateFormatSelect.UsFormat)).ToBeVisibleAsync();
        await Expect(settings.DatesPreferencesLink).ToHaveAttributeAsync("href", AccountLink());
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.DottedFormat);

        await App.GotoAsync(Routes.KeyValue(key));
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.ReformatIsoDateTime(shown, DateFormatSelect.UsFormat));

        await account.OpenPreferencesAsync();
        await account.PreferredDateFormat.SelectProjectDefaultAsync();

        await settings.OpenAsync();
        await settings.DateFormat.ExpectFormatAsync(DateFormatSelect.DottedFormat);
        await Expect(settings.PersonalDateFormatNote(DateFormatSelect.UsFormat)).ToHaveCountAsync(0);
        await App.GotoAsync(Routes.KeyValue(key));
        await Expect(updated).ToHaveTextAsync(DateFormatSelect.ReformatIsoDateTime(shown, DateFormatSelect.DottedFormat));
    }

    // SET-PRF-03: Personal type names in Preferences win over the project's on Templates and Annotations.
    // The project names Execution Job and colors it red; the personal name Run wins on Templates and in New
    // annotation's Type options, while the project's color stays (each field resolves personal, then project, then
    // built-in), and Settings point to Preferences; after "Reset types" Templates show Job again.
    [Test]
    [TestCase(137)]
    [UserRole(Role.Editor)]
    public async Task Preferences_PersonalTypeName_WinsOverProject()
    {
        const string execution = PlatformData.Execution;

        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        await settings.ProjectTypes.SetNameAsync(execution, "Job");
        await settings.ProjectTypes.PickColorAsync(execution, "Red");
        await App.ExpectTypeColorAsync(execution, Red);

        var account = new AccountPage(App, Routes);
        await account.OpenPreferencesAsync();
        await account.PersonalTypes.SetNameAsync(execution, "Run");
        await Expect(account.ResetTypesButton).ToBeEnabledAsync();

        var templates = new ModelingTemplatePage(App, Routes);
        await templates.OpenListAsync();
        await Expect(templates.ListRow(execution).GetByText("Run", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(App.Page.GetByText("Job", new() { Exact = true })).ToHaveCountAsync(0);
        await App.ExpectTypeColorAsync(execution, Red);

        await settings.OpenAsync();
        await Expect(settings.PersonalTypesNote).ToBeVisibleAsync();
        await Expect(settings.TypesPreferencesLink).ToHaveAttributeAsync("href", AccountLink());
        await Expect(settings.ProjectTypes.Name(execution)).ToHaveValueAsync("Job");

        // Nothing is created: the next page load closes the dialog without a request.
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var dialog = await annotations.OpenNewAnnotationAsync();
        await dialog.TypeSelect.ClickAsync();
        await Expect(dialog.TypeOption("Run")).ToHaveCountAsync(1);
        await Expect(dialog.TypeOption("Execution")).ToHaveCountAsync(0);
        await Expect(dialog.TypeOption("Job")).ToHaveCountAsync(0);

        await account.OpenPreferencesAsync();
        await account.ResetTypesButton.ClickAsync();
        await Expect(account.ResetTypesButton).ToBeDisabledAsync();

        await templates.OpenListAsync();
        await Expect(templates.ListRow(execution).GetByText("Job", new() { Exact = true })).ToBeVisibleAsync();
    }

    // The defaults of a fresh browser: Compact, JSON, Form, "Project default", and no personal type.
    private static async Task ExpectDefaultPreferencesAsync(AccountPage account)
    {
        await Expect(account.RowDensityTab("Compact")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(account.DocumentFormatTab("JSON")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(account.AnnotationEditingTab("Form")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(account.PreferredDateFormat.Combobox).ToHaveTextAsync(DateFormatSelect.ProjectDefault);
        await Expect(account.ResetTypesButton).ToBeDisabledAsync();
    }

    // "2026-11-02 10:15 GMT+1": the date as yyyy-mm-dd, the 24-hour time and the zone.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \S+$")]
    private static partial Regex IsoDateTime();

    // The link "Open Preferences" goes to /account.
    [GeneratedRegex("/account$")]
    private static partial Regex AccountLink();
}
