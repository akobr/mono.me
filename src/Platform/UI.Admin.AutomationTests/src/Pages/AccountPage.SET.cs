using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the account page: the Preferences card of this browser (the tabs of Row density
// "Compact" / "Comfortable", Document format "JSON" / "YAML" and Annotation editing "Form" / "JSON" / "YAML", the
// selected one with aria-selected="true"; the personal "Date format" with "Project default"; "Reset preferences") and
// the personal "Annotation types" ("Reset types", disabled while no type is changed), all kept in localStorage
// '2s-admin:preferences'. A personal choice wins over the project's. The Theme tabs belong to the Navigation section.
public sealed partial class AccountPage
{
    // The localStorage key of the preferences (stores/preferences.ts).
    public const string PreferencesKey = "2s-admin:preferences";

    public ILocator PreferencesHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Preferences", Exact = true, Level = 2 });

    // The personal date format; "Project default" follows the Settings of each project.
    public DateFormatSelect PreferredDateFormat => new(app, PreferencesCard.GetByRole(AriaRole.Combobox, new() { Name = "Date format", Exact = true }));

    public ILocator ResetPreferencesButton => PreferencesCard.GetByRole(AriaRole.Button, new() { Name = "Reset preferences", Exact = true });

    // The personal names, colors and icons of the types, in every project; the fields' placeholders are the built-in
    // values, because the page has no project.
    public TypeAppearanceEditor PersonalTypes => new(app, AnnotationTypesCard);

    public ILocator ResetTypesButton => AnnotationTypesCard.GetByRole(AriaRole.Button, new() { Name = "Reset types", Exact = true });

    // Two tab lists have "JSON" and "YAML", so each tab is found in its own list: Row density has "Compact", Annotation
    // editing "Form", and Document format "YAML" without "Form".
    private ILocator RowDensityTabs => PreferencesCard.GetByRole(AriaRole.Tablist).Filter(new() { Has = PreferenceTab("Compact") });

    private ILocator DocumentFormatTabs => PreferencesCard.GetByRole(AriaRole.Tablist)
        .Filter(new() { Has = PreferenceTab("YAML"), HasNot = PreferenceTab("Form") });

    private ILocator AnnotationEditingTabs => PreferencesCard.GetByRole(AriaRole.Tablist).Filter(new() { Has = PreferenceTab("Form") });

    // "Compact" or "Comfortable".
    public ILocator RowDensityTab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return RowDensityTabs.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // "JSON" or "YAML": the default format of the configuration, template and schema editors.
    public ILocator DocumentFormatTab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return DocumentFormatTabs.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // "Form", "JSON" or "YAML": the default mode of the annotation detail.
    public ILocator AnnotationEditingTab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AnnotationEditingTabs.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // Opens the account page and waits for its Preferences card.
    public async Task OpenPreferencesAsync()
    {
        await OpenAsync();
        await Expect(PreferencesHeading).ToBeVisibleAsync();
    }

    // Clicks a tab of Preferences (RowDensityTab, DocumentFormatTab, AnnotationEditingTab) and waits until it is the
    // selected one.
    public async Task SelectPreferenceTabAsync(ILocator tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        await tab.ClickAsync();
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");
    }

    // Waits until this browser keeps the preference's value at the path ("density"; "types", "exe", "label"), or none
    // for null.
    public async Task ExpectStoredPreferenceAsync(string? expected, params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await app.ExpectStoredSettingAsync(PreferencesKey, path, expected);
    }

    private ILocator PreferenceTab(string name)
    {
        return app.Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }
}
