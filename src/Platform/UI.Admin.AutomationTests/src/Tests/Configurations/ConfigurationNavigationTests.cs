using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// List and Navigation: the Configurations list (a grid from 768 px, a list of buttons on a phone) with the version and
// the author of each configuration, its detail opened and closed, the detail's tabs keeping the list's filter, and the
// page memory that brings the user back to the last configuration and tab of the section.
[Area(Area.Configurations)]
public sealed partial class ConfigurationNavigationTests : PlatformTest
{
    // CFG-NAV-01: A saved configuration is listed with its version and author, and its row opens the detail.
    // After Create empty and a save (v2) the filtered list counts 1 configuration; the grid's row shows the type's
    // name, the Name, the key, v2, the signed-in user as the author and "just now" (a phone's item the name, v2 and the
    // key). The row's Name cell opens the detail with the filter kept: beside the list from 768 px, over it on a phone.
    [Test]
    [TestCase(228)]
    [UserRole(Role.Editor)]
    public async Task Configurations_List_ShowsVersionAndOpensDetail()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var filter = $"key:{key}";
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenFilteredAsync(key, filter);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, PlatformData.ConfigurationDocument());

        await configuration.ConfigurationPanel.CloseAsync();
        await App.ExpectAtAsync(Routes.Configurations);
        await App.ExpectQueryAsync("q", filter);
        var list = new ConfigurationsPage(App, Routes);
        await Expect(list.FilteredCount).ToHaveTextAsync(OneOfTotal());

        var item = list.Item(key);
        var name = key.Split('.')[^1];
        if (list.HasGridLayout)
        {
            var author = await App.SignedInNameAsync();
            await Expect(list.ItemCell(key, ConfigurationsPage.TypeColumn)).ToHaveTextAsync("Responsibility");
            await Expect(list.ItemCell(key, ConfigurationsPage.NameColumn)).ToHaveTextAsync(name);
            await Expect(list.ItemCell(key, ConfigurationsPage.KeyColumn)).ToHaveTextAsync(key);
            await Expect(list.ItemCell(key, ConfigurationsPage.VersionColumn)).ToHaveTextAsync("v2");
            await Expect(list.ItemCell(key, ConfigurationsPage.AuthorColumn)).ToHaveTextAsync(author);
            await Expect(list.ItemCell(key, ConfigurationsPage.UpdatedColumn)).ToHaveTextAsync("just now");
        }
        else
        {
            // The item's own spans, matched exactly: its key line ("rst.<name>") contains the name and maybe "v2" too.
            await Expect(item.GetByText(name, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(item.GetByText("v2", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(item.GetByText(key, new() { Exact = true })).ToBeVisibleAsync();
        }

        await list.OpenItemAsync(item);
        await App.ExpectAtAsync(Routes.Configuration(key));
        await App.ExpectQueryAsync("q", filter);
        await Expect(configuration.Region(key)).ToBeVisibleAsync();
        await Expect(configuration.EditTab).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(2)).ToBeVisibleAsync();

        var panel = configuration.ConfigurationPanel;
        if (list.HasGridLayout)
        {
            await Expect(list.Filter).ToBeVisibleAsync();
            await Expect(panel.CloseButton).ToBeVisibleAsync();
        }
        else
        {
            await Expect(list.Filter).ToBeHiddenAsync();
            await Expect(item).ToBeHiddenAsync();
            await Expect(panel.BackButton).ToBeVisibleAsync();
        }
    }

    // CFG-NAV-02: The tabs keep the filter, and Close returns to the list.
    // Opened with the run's filter, every tab link (Effective, Resolved, Versions, Schema, back to Edit) keeps ?q= and
    // shows its content; Close returns to the list with the filter in its box and the key's row. The run's filter is
    // shared by the run's tests, so the count is not checked, and the virtualized grid is scrolled to the key's row
    // (ExpectListedAsync). The mock has no Responsibility type schema; a deployed target may have one.
    [Test]
    [TestCase(229)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Tabs_KeepFilterAndCloseToList()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var filter = PlatformData.RunSlug;
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        await configuration.OpenFilteredAsync(key, filter);
        await Expect(configuration.Region(key)).ToBeVisibleAsync();
        await Expect(configuration.JsonMode).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.OpenTabAsync("Effective");
        await ExpectAtFilteredAsync(Routes.ConfigurationTab(key, "effective"), filter);
        var effective = configuration.EffectiveView;
        await Expect(effective.MergeOrder).ToBeVisibleAsync();
        await Expect(effective.Document.Content).ToBeVisibleAsync();

        await configuration.OpenTabAsync("Resolved");
        await ExpectAtFilteredAsync(Routes.ConfigurationTab(key, "resolved"), filter);
        var resolved = configuration.ResolvedView;
        await Expect(resolved.SecretsAlertTitle).ToBeVisibleAsync();
        await Expect(resolved.RevealButton).ToBeVisibleAsync();

        await configuration.OpenTabAsync("Versions");
        await ExpectAtFilteredAsync(Routes.ConfigurationTab(key, "versions"), filter);
        await configuration.Versions.WaitForTimelineAsync();
        await Expect(configuration.Versions.CurrentBadge(1)).ToBeVisibleAsync();

        await configuration.OpenTabAsync("Schema");
        await ExpectAtFilteredAsync(Routes.ConfigurationTab(key, "schema"), filter);
        var schema = configuration.CombinedSchema;
        await Expect(Target.IsMock ? schema.NoSchemaHeading : schema.NoSchemaHeading.Or(schema.AppliedSchemasHeading)).ToBeVisibleAsync();

        await configuration.OpenEditTabAsync(key);
        await App.ExpectQueryAsync("q", filter);
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.ConfigurationPanel.CloseAsync();
        await ExpectAtFilteredAsync(Routes.Configurations, filter);
        var list = new ConfigurationsPage(App, Routes);
        await Expect(list.Filter).ToHaveValueAsync(filter);
        await list.ExpectListedAsync(key);
    }

    // CFG-NAV-03: Returning to Configurations reopens the last configuration and tab.
    // The section remembers its last location in memory (not across a page load): from Annotations, the sidebar's
    // Configurations returns to the configuration's Versions tab with its filter. From within the section the plain
    // list stays plain: after Close, Configurations opens the list without a filter. Desktop only: the full-screen
    // detail of a phone has no navbar to open the navigation from.
    [Test]
    [TestCase(230)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task Configurations_Section_ReopensLastConfiguration()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var filter = $"key:{key}";
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        await App.GotoAsync(AppRoutes.WithQuery(Routes.ConfigurationTab(key, "versions"), ("q", filter)));
        await configuration.Versions.WaitForTimelineAsync();
        await Expect(configuration.Versions.CurrentBadge(1)).ToBeVisibleAsync();

        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);

        await App.NavigateAsync("Configurations");
        await ExpectAtFilteredAsync(Routes.ConfigurationTab(key, "versions"), filter);
        await Expect(configuration.Versions.CurrentBadge(1)).ToBeVisibleAsync();

        await configuration.ConfigurationPanel.CloseAsync();
        await ExpectAtFilteredAsync(Routes.Configurations, filter);

        await App.NavigateAsync("Configurations");
        await App.ExpectAtAsync(Routes.Configurations);
        await App.ExpectQueryAsync("q", null);
        await Expect(App.Page).ToHaveURLAsync(PlainList());
        await Expect(new ConfigurationsPage(App, Routes).Filter).ToHaveValueAsync(string.Empty);
    }

    // "1 of <total>", the total in the browser's number format.
    [GeneratedRegex(@"^1 of \d[\d,.\s]*$")]
    private static partial Regex OneOfTotal();

    // The list without any query.
    [GeneratedRegex(@"/configurations$")]
    private static partial Regex PlainList();

    // The page is at the path with exactly the filter in ?q=.
    private async Task ExpectAtFilteredAsync(string path, string filter)
    {
        await App.ExpectAtAsync(path);
        await App.ExpectQueryAsync("q", filter);
    }
}
