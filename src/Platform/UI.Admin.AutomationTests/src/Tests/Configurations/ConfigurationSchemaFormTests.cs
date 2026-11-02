using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Schema and Form: how a configuration follows its schema. Each test gives its own new annotation an annotation schema
// in Modeling (it applies to that annotation only, so nothing project-wide changes) before its configuration exists,
// and opens the configuration with a page load afterwards (the app keeps a combined schema for 5 minutes). A deployed
// target's Responsibility or Unit type schema could add a layer or fields, so the tests find fields by their JSON
// Pointer and layers by their label, never by counts.
[Area(Area.Configurations)]
public sealed partial class ConfigurationSchemaFormTests : PlatformTest
{
    private const string RetriesAndEnabledSchema = """
        {"type":"object","properties":{"retries":{"type":"integer","minimum":0,"maximum":10,"title":"Retries"},"enabled":{"type":"boolean","title":"Enabled"}},"required":["retries"]}
        """;

    private const string RetriesMaximumSchema = """
        {"type":"object","properties":{"retries":{"type":"integer","maximum":10}}}
        """;

    private const string RetriesSchema = """
        {"type":"object","properties":{"retries":{"type":"integer","title":"Retries"}}}
        """;

    private const string TiersAndHeadersSchema = """
        {"type":"object","properties":{"tiers":{"type":"array","title":"Tiers","items":{"type":"object","properties":{"name":{"type":"string","title":"Name"},"limit":{"type":"integer","title":"Limit"}}}},"headers":{"type":"object","title":"Headers","additionalProperties":{"type":"string"}}}}
        """;

    // CFG-SCH-01: The form follows the annotation's schema, and the Schema tab lists it.
    // With an annotation schema the Edit tab opens in Form mode: a required, still empty "Retries *" makes "The form has
    // errors.", a number and a switch count as changes and save as v2, JSON shows the values, and the Schema tab lists
    // the Annotation layer (v1, a link to its schema in Modeling) with the combined schema.
    [Test]
    [TestCase(159)]
    [UserRole(Role.Editor)]
    public async Task Configurations_SchemaForm_SavesValue()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        await CreateAnnotationSchemaAsync(key, RetriesAndEnabledSchema);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.FormMode).ToBeCheckedAsync();
        await Expect(configuration.JsonMode).ToBeVisibleAsync();
        await Expect(configuration.YamlMode).ToBeVisibleAsync();
        var form = configuration.Form;
        await Expect(form.Label("/retries")).ToHaveTextAsync("Retries *");
        await Expect(form.Label("/enabled")).ToHaveTextAsync("Enabled");
        await Expect(configuration.FormErrors).ToBeVisibleAsync();

        var retries = form.NumberInput("/retries");
        await form.FillAsync(retries, "4");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();
        await Expect(configuration.FormErrors).ToHaveCountAsync(0);

        await form.Switch("/enabled").ClickAsync();
        await Expect(form.Switch("/enabled")).ToBeCheckedAsync();
        await Expect(configuration.Changes(2)).ToBeVisibleAsync();

        await configuration.SaveWithShortcutAsync(retries, 2);

        await configuration.JsonMode.ClickAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"retries\": 4");
        await Expect(configuration.Editor).ToContainTextAsync("\"enabled\": true");

        await configuration.OpenTabAsync("Schema");
        var schema = configuration.CombinedSchema;
        await Expect(schema.AppliedSchemasHeading).ToBeVisibleAsync();
        await Expect(schema.LayerText(key, "Annotation")).ToBeVisibleAsync();
        await Expect(schema.LayerLink(key)).ToHaveAttributeAsync("href", new Regex($@"\/modeling\/schemas\/annotation\/{Regex.Escape(key)}$"));
        await Expect(schema.LayerText(key, "v1")).ToBeVisibleAsync();
        await Expect(schema.Combined.Content).ToContainTextAsync("retries");
    }

    // CFG-SCH-02: A schema violation lists the errors, and Save anyway stores the document.
    // A value over the schema's maximum is refused by the API (409): the alert lists /retries and nothing is saved.
    // Save anyway asks first (Cancel keeps the alert and v1), then stores the document as v2.
    [Test]
    [TestCase(169)]
    [UserRole(Role.Editor)]
    public async Task Configurations_SchemaViolation_SaveAnywayStoresIt()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        await CreateAnnotationSchemaAsync(key, RetriesMaximumSchema);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        await configuration.SetDocumentAsync("""{"retries": 42}""");
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();
        await Expect(configuration.SaveButton).ToBeEnabledAsync();

        await configuration.Document.PressSaveAsync();
        await Expect(configuration.SchemaErrors).ToBeVisibleAsync();
        await Expect(configuration.SchemaErrors.GetByText("/retries", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(App.Toast(SavedToast())).ToHaveCountAsync(0);
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        var confirmation = configuration.SaveAnywayDialog;
        await configuration.SaveAnywayButton.ClickAsync();
        await confirmation.ExpectOpenAsync();
        await Expect(confirmation.Description).ToHaveTextAsync(OneViolation());

        await confirmation.CancelAsync();
        await Expect(configuration.SchemaErrors).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.SaveAnywayButton.ClickAsync();
        await confirmation.ConfirmAsync("Save anyway");
        await Expect(App.Toast("Configuration saved (v2)")).ToBeVisibleAsync();
        await Expect(configuration.SchemaErrors).ToHaveCountAsync(0);
        await Expect(configuration.ChangeCounter).ToHaveCountAsync(0);
    }

    // CFG-SCH-03: An inherited value shows as a placeholder, with Override and Reset to inherited.
    // A unit inherits retries 2 from its responsibility: the unit's form shows it as the placeholder with the badge
    // "inherited". Override copies it into the draft, Reset to inherited drops it again, and once the override is saved
    // the value is the unit's own (no badge, Override or Reset to inherited any more).
    [Test]
    [TestCase(174)]
    [UserRole(Role.Editor)]
    public async Task Configurations_InheritedValue_OverrideAndReset()
    {
        var unit = PlatformData.AnnotationKey(PlatformData.Unit);
        var parent = PlatformData.AncestorKey(unit, PlatformData.Responsibility);
        await ConfigurationSteps.BulkCreateAsync(App, Routes, unit);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(parent);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"retries": 2}""");
        await CreateAnnotationSchemaAsync(unit, RetriesSchema);

        await configuration.OpenAsync(unit);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.FormMode.ClickAsync();
        await App.ExpectQueryAsync("format", "form");

        var form = configuration.Form;
        var retries = form.NumberInput("/retries");
        await Expect(form.InheritedBadge("/retries")).ToBeVisibleAsync();
        await Expect(retries).ToHaveAttributeAsync("placeholder", "2");
        await Expect(form.OverrideButton("/retries")).ToBeVisibleAsync();

        await form.OverrideButton("/retries").ClickAsync();
        await Expect(retries).ToHaveValueAsync("2");
        await Expect(form.ResetToInheritedButton("/retries")).ToBeVisibleAsync();
        await Expect(form.InheritedBadge("/retries")).ToHaveCountAsync(0);
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await form.ResetToInheritedButton("/retries").ClickAsync();
        await Expect(form.InheritedBadge("/retries")).ToBeVisibleAsync();
        await Expect(form.OverrideButton("/retries")).ToBeVisibleAsync();
        await Expect(retries).ToHaveValueAsync(string.Empty);
        await Expect(retries).ToHaveAttributeAsync("placeholder", "2");
        await Expect(configuration.ChangeCounter).ToHaveCountAsync(0);

        await form.OverrideButton("/retries").ClickAsync();
        await configuration.SaveWithShortcutAsync(retries, 2);
        await Expect(form.InheritedBadge("/retries")).ToHaveCountAsync(0);
        await Expect(form.OverrideButton("/retries")).ToHaveCountAsync(0);
        await Expect(form.ResetToInheritedButton("/retries")).ToHaveCountAsync(0);

        await configuration.JsonMode.ClickAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"retries\": 2");
    }

    // CFG-SCH-04: Lists and maps of the form add nested values.
    // An array of objects gets a card per item (titled "Item 1", then by its first text, "gold") and a map a row per
    // key (x-run); each new container is one change, both save as v2, and JSON shows the nested values.
    [Test]
    [TestCase(185)]
    [UserRole(Role.Editor)]
    public async Task Configurations_FormListsAndMaps_SaveNestedValues()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        await CreateAnnotationSchemaAsync(key, TiersAndHeadersSchema);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.FormMode).ToBeCheckedAsync();

        var form = configuration.Form;
        await Expect(form.Label("/tiers")).ToHaveTextAsync("Tiers");
        await Expect(form.Button("/tiers", "Add item")).ToBeVisibleAsync();
        await Expect(form.Label("/headers")).ToHaveTextAsync("Headers");
        var newKey = form.Field("/headers").GetByPlaceholder("New key", new() { Exact = true });
        await Expect(newKey).ToBeVisibleAsync();
        await Expect(form.Button("/headers", "Add")).ToBeDisabledAsync();

        await form.Button("/tiers", "Add item").ClickAsync();
        await Expect(form.Button("/tiers", "Item 1")).ToBeVisibleAsync();
        await Expect(form.Label("/tiers/0/name")).ToHaveTextAsync("Name");
        await Expect(form.Label("/tiers/0/limit")).ToHaveTextAsync("Limit");
        await Expect(form.Button("/tiers", "Move up")).ToBeVisibleAsync();
        await Expect(form.Button("/tiers", "Move down")).ToBeVisibleAsync();
        await Expect(form.Button("/tiers", "Remove")).ToBeVisibleAsync();

        await form.FillAsync(form.TextInput("/tiers/0/name"), "gold");
        await Expect(form.Button("/tiers", "gold")).ToBeVisibleAsync();
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();

        await newKey.FillAsync("x-run");
        await form.Button("/headers", "Add").ClickAsync();
        await Expect(form.Field("/headers").GetByRole(AriaRole.Textbox, new() { Name = "Key", Exact = true })).ToHaveValueAsync("x-run");
        await Expect(form.Field("/headers/x-run")).ToBeVisibleAsync();
        await Expect(form.Button("/headers", "Remove x-run")).ToBeVisibleAsync();
        await Expect(configuration.Changes(2)).ToBeVisibleAsync();

        var value = form.TextInput("/headers/x-run");
        await form.FillAsync(value, PlatformData.RunSlug);
        await configuration.SaveWithShortcutAsync(value, 2);

        await configuration.JsonMode.ClickAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"name\": \"gold\"");
        await Expect(configuration.Editor).ToContainTextAsync($"\"x-run\": \"{PlatformData.RunSlug}\"");
    }

    // "Configuration saved (v<n>)" of any version.
    [GeneratedRegex(@"^Configuration saved \(v\d+\)$")]
    private static partial Regex SavedToast();

    // The confirmation of Save anyway with the one violation of /retries; the message after the pointer is the
    // server's (a humanized NJsonSchema kind, "Number too big" on the mock).
    [GeneratedRegex(@"^The configuration will be stored with 1 schema violation\(s\): \/retries .+\.$")]
    private static partial Regex OneViolation();

    // Creates the annotation schema of the key in Modeling (toast "Schema saved (v1)"); it applies to that annotation
    // and its descendants only.
    private async Task CreateAnnotationSchemaAsync(string key, string schema)
    {
        var schemas = new ModelingSchemaPage(App, Routes);
        await schemas.OpenAnnotationAsync(key);
        await schemas.CreateAsync(schema);
    }
}
