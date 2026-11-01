using System.Text.Json.Nodes;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

[Area(Area.Configurations)]
public sealed class ConfigurationsTests : PlatformTest
{
    // Bulk create, Create empty and Save are Contributor and up.
    protected override UserRequirement RequiredUser => EditingUser;

    // A new responsibility (Bulk create) gets an empty configuration, then a generated document in the JSON editor;
    // after the save the Effective tab shows every value of the document. A responsibility has no ancestors and,
    // usually, no type schema or template, so the test depends on none of the target's modeling.
    [Test]
    [Elementary]
    [TestCase(3)]
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
        await configuration.SaveAsync();
        await Expect(App.Toast("Configuration saved (v2)")).ToBeVisibleAsync();

        var effective = await configuration.OpenEffectiveAsync();
        foreach (var (name, value) in JsonNode.Parse(document)!.AsObject())
        {
            await Expect(effective).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }
    }
}
