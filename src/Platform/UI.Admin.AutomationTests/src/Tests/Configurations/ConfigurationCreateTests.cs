using System.Text.Json.Nodes;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Create: an own configuration of an annotation, from empty or from the type's template, and its first saves.
[Area(Area.Configurations)]
public sealed class ConfigurationCreateTests : PlatformTest
{
    // CFG-CRE-01: Create empty, save a JSON document, and the Effective tab shows it.
    // A new responsibility (Bulk create) gets an empty configuration, then a generated document in the JSON editor;
    // after the save (version 2) the Effective tab shows every value of the document. A responsibility has no ancestors
    // and, usually, no type schema or template, so the test depends on none of the target's modeling.
    [Test]
    [Elementary]
    [TestCase(3)]
    [UserRole(Role.Editor)]
    public async Task Configurations_SetDocument_EffectiveShowsIt()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var document = PlatformData.ConfigurationDocument();

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        await configuration.SetDocumentAsync(document);
        await configuration.SaveAsync(2);

        var effective = await configuration.OpenEffectiveAsync();
        foreach (var (name, value) in JsonNode.Parse(document)!.AsObject())
        {
            await Expect(effective).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }
    }
}
