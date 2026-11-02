using System.Text.Json.Nodes;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Roles: the role gates of the configurations at their boundaries. A Contributor creates and edits, but the Resolved
// tab leaves secret values out; a Reader gets no create action and sees a configuration read-only. Each test acts in
// the project that Target:RoleProjects maps its role to (on the mock acme/shipping for the Contributor and
// northwind/portal for the Reader, where Ada Lovelace holds exactly that role), so every page object uses Routes.
[Area(Area.Configurations)]
public sealed class ConfigurationRoleTests : PlatformTest
{
    // CFG-ROL-01: A Contributor creates and saves a configuration, and Resolved leaves secret values out.
    // A Contributor gets Create empty and Start from template, then Delete own and no "Read-only"; a generated
    // document saves as v2. Resolved tells the Contributor that secret values are left out (ContributorWithSecrets sees
    // them), and Reveal shows every value of the document.
    [Test]
    [TestCase(231)]
    [UserRole(Role.Contributor)]
    public async Task Configurations_Contributor_EditsWithoutSecrets()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var document = PlatformData.ConfigurationDocument();

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(configuration.CreateEmptyButton).ToBeVisibleAsync();
        await Expect(configuration.StartFromTemplateButton).ToBeVisibleAsync();

        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.DeleteOwnButton).ToBeVisibleAsync();
        await Expect(configuration.ReadOnlyBadge).ToHaveCountAsync(0);

        await configuration.SetDocumentAsync(document);
        await configuration.SaveAsync(2);

        await configuration.OpenTabAsync("Resolved");
        var resolved = configuration.ResolvedView;
        await resolved.WaitForResolvedAsync();
        await Expect(resolved.SecretsAlertTitle).ToBeVisibleAsync();
        await Expect(resolved.SecretsAlertDescription).ToHaveTextAsync(ConfigurationResolved.SecretsLeftOutText);

        var revealed = await resolved.RevealAsync();
        foreach (var (name, value) in JsonNode.Parse(document)!.AsObject())
        {
            await Expect(revealed.Content).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }
    }

    // CFG-ROL-02: A Reader gets no New and no create actions.
    // The Reader's list has the filter but no "New configuration". A Reader cannot create anything, so the detail opens
    // a key that does not exist: the empty state explains the inheritance without Create empty or Start from template,
    // Effective says nothing is configured or inherited, and Versions has no history.
    [Test]
    [TestCase(232)]
    [UserRole(Role.Reader)]
    public async Task Configurations_Reader_HasNoCreateActions()
    {
        var list = new ConfigurationsPage(App, Routes);
        await list.OpenAsync();
        await Expect(list.FirstItem.Or(list.EmptyStateHeading)).ToBeVisibleAsync();
        await Expect(list.Filter).ToBeVisibleAsync();
        await Expect(list.NewConfigurationButtons).ToHaveCountAsync(0);

        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(configuration.NoConfigurationDescription).ToBeVisibleAsync();
        await Expect(configuration.CreateEmptyButton).ToHaveCountAsync(0);
        await Expect(configuration.StartFromTemplateButton).ToHaveCountAsync(0);

        await configuration.OpenTabAsync("Effective");
        var effective = configuration.EffectiveView;
        await Expect(effective.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(effective.NothingInherited).ToBeVisibleAsync();

        await configuration.OpenTabAsync("Versions");
        await Expect(configuration.NoHistoryHeading).ToBeVisibleAsync();
    }

    // CFG-ROL-03: A Reader sees an existing configuration read-only.
    // The first configuration of the Reader's seeded list opens with the badge "Read-only" and without Delete own,
    // Revert and Save; its JSON editor cannot be edited, and Resolved leaves secret values out. Mock only: a Reader
    // cannot create a configuration, and a deployed Reader project's content is unknown.
    [Test]
    [TestCase(233)]
    [UserRole(Role.Reader)]
    [MockOnly("a Reader cannot create a configuration, so the test opens one of the seed of northwind/portal")]
    public async Task Configurations_Reader_SeesConfigurationReadOnly()
    {
        var list = new ConfigurationsPage(App, Routes);
        await list.OpenAsync();
        await list.OpenItemAsync(list.FirstItem);

        var configuration = new ConfigurationDetail(App, Routes);
        await Expect(configuration.ReadOnlyBadge).ToBeVisibleAsync();
        await Expect(configuration.DeleteOwnButton).ToHaveCountAsync(0);
        await Expect(configuration.RevertButton).ToHaveCountAsync(0);
        await Expect(configuration.SaveButton).ToHaveCountAsync(0);

        // A schema may apply, so the configuration can open in Form mode.
        await configuration.JsonMode.ClickAsync();
        await Expect(configuration.Editor).ToBeVisibleAsync();
        await Expect(configuration.Editor).Not.ToBeEditableAsync();

        await configuration.OpenTabAsync("Resolved");
        var resolved = configuration.ResolvedView;
        await Expect(resolved.SecretsAlertTitle).ToBeVisibleAsync();
        await Expect(resolved.SecretsAlertDescription).ToHaveTextAsync(ConfigurationResolved.SecretsLeftOutText);
    }
}
