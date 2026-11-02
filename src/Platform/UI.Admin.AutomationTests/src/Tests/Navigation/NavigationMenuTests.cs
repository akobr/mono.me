using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Navigation Menu: the sidebar (docked, the icon rail or the slideover), its sections and links, and their roles.
[Area(Area.Navigation)]
public sealed class NavigationMenuTests : PlatformTest
{
    // The lock badge of a menu entry that the role may only read or not open.
    private const string LockBadge = "[data-slot=\"linkTrailingBadge\"]";

    // NAV-MNU-01: Navigation menu opens the pages of the view, the project and the app.
    // From the annotations, the navigation (the docked sidebar on a wide screen, the slideover behind "Open
    // navigation" on a phone or a tablet) opens pages of the view, the project and outside, and back: each with its
    // route and its own content.
    [Test]
    [Elementary]
    [TestCase(6)]
    public async Task Navigation_MobileMenu_OpensPages()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        (string Link, string Path, ILocator Content)[] pages =
        [
            ("Configurations", Routes.Configurations, new ConfigurationsPage(App, Routes).Filter),
            ("Values store", Routes.KeyValues, new KeyValuesPage(App, Routes).Search),
            ("Settings", Routes.Settings(), new ProjectSettingsPage(App, Routes).ProjectHeading),
            ("Organizations", Routes.Organizations, new OrganizationsPage(App, Routes).Heading),
            ("Annotations", Routes.Annotations, annotations.Filter),
        ];

        foreach (var (link, path, content) in pages)
        {
            await App.NavigateAsync(link);
            await App.ExpectAtAsync(path);
            await Expect(content).ToBeVisibleAsync();
        }
    }

    // NAV-MNU-02: Pages outside the view keep the sections of the last view.
    // After a view of the project, the project's settings, the organization's overview, the organizations and the
    // account keep the menu's view section of that view (its label and its links), so "Annotations" leads back into it.
    // The view is a generated name that is never registered: views are implicit, so opening it writes nothing.
    [Test]
    [TestCase(22)]
    public async Task Navigation_OtherPages_KeepLastViewSections()
    {
        var view = Routes with { View = PlatformData.ViewName() };

        await App.GotoAsync(view.Configurations);
        await App.WaitForReadyAsync();
        await Expect(new ConfigurationsPage(App, view).EmptyViewHeading).ToBeVisibleAsync();
        var menu = await App.MenuAsync();
        var label = App.NavigationViewLabel(menu, view.View);
        await Expect(label).ToBeVisibleAsync();
        await Expect(label.GetByText("prod", new() { Exact = true })).ToHaveCountAsync(0);
        await App.CloseMenuAsync();

        (string Link, string Path)[] pages =
        [
            ("Settings", Routes.Settings()),
            ("Overview", Routes.OrganizationOverview),
            ("Organizations", Routes.Organizations),
            ("Account", Routes.Account),
        ];

        foreach (var (link, path) in pages)
        {
            await App.NavigateAsync(link);
            await App.ExpectAtAsync(path);
            menu = await App.MenuAsync();
            await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Annotations", Exact = true })).ToHaveAttributeAsync("href", $"/{view.Annotations}");
            await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveAttributeAsync("href", $"/{view.Settings()}");
            await Expect(App.NavigationViewLabel(menu, view.View)).ToBeVisibleAsync();
            await App.CloseMenuAsync();
        }

        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(view.Annotations);
        await Expect(new AnnotationsPage(App, view).EmptyState).ToBeVisibleAsync();
    }

    // NAV-MNU-03: An Administrator's menu lists every entry without locks.
    // An Administrator (or Owner) of the project gets the five view entries in their order without a lock, every page of
    // the project and none of the old pages merged into others, the organization's overview and the global entries;
    // the palette offers Modeling's Export & Import tab and opens it.
    [Test]
    [TestCase(24)]
    [UserRole(Role.Admin)]
    public async Task Navigation_AdministratorMenu_ListsEveryEntryUnlocked()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var menu = await App.MenuAsync();
        var viewEntries = ViewEntries(menu);
        await Expect(viewEntries).ToHaveTextAsync(["Annotations", "Configurations", "Values store", "Graph", "Modeling"]);
        await Expect(viewEntries.Nth(2)).ToHaveAttributeAsync("href", $"/{Routes.KeyValues}");
        await Expect(viewEntries.Locator(LockBadge)).ToHaveCountAsync(0);

        foreach (var path in new[] { Routes.Views, Routes.Members, Routes.Machines, Routes.Settings(), Routes.Monitor() })
        {
            await Expect(App.NavigationMenuLink(menu, path)).ToHaveCountAsync(1);
        }

        foreach (var page in new[] { "invitations", "certificates", "secrets", "audit", "usage" })
        {
            await Expect(App.NavigationMenuLink(menu, Routes.OldProjectPage(page))).ToHaveCountAsync(0);
        }

        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Overview", Exact = true })).ToHaveAttributeAsync("href", $"/{Routes.OrganizationOverview}");
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true })).ToHaveAttributeAsync("href", $"/{Routes.Organizations}");
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Account", Exact = true })).ToHaveAttributeAsync("href", $"/{Routes.Account}");
        await App.CloseMenuAsync();

        // The Views page has no catalog, so no annotation of a live catalog ranks before the tab.
        await App.NavigateAsync("Views");
        await App.ExpectAtAsync(Routes.Views);
        var search = await App.OpenPaletteAsync();
        await search.FillAsync("Export & Import");
        await App.PickFirstPaletteOptionAsync("Export & ImportModeling");
        await App.ExpectAtAsync(Routes.ModelingTab("export-import"));
    }

    // NAV-MNU-04: A Contributor's menu leaves out Members, Monitor and Export & Import.
    // A Contributor of the project gets the five view entries without a lock, Views, Machines and Settings of the
    // project but neither Members nor Monitor, and in the palette the editor actions and the Modeling tabs without
    // Export & Import (an Administrator's tab).
    [Test]
    [TestCase(27)]
    [UserRole(Role.Contributor)]
    public async Task Navigation_ContributorMenu_HidesAdministration()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.WaitForCatalogAsync();

        var menu = await App.MenuAsync();
        var viewEntries = ViewEntries(menu);
        await Expect(viewEntries).ToHaveTextAsync(["Annotations", "Configurations", "Values store", "Graph", "Modeling"]);
        await Expect(viewEntries.Locator(LockBadge)).ToHaveCountAsync(0);

        foreach (var path in new[] { Routes.Views, Routes.Machines, Routes.Settings() })
        {
            await Expect(App.NavigationMenuLink(menu, path)).ToHaveCountAsync(1);
        }

        await Expect(App.NavigationMenuLink(menu, Routes.Members)).ToHaveCountAsync(0);
        await Expect(App.NavigationMenuLink(menu, Routes.Monitor())).ToHaveCountAsync(0);
        await App.CloseMenuAsync();

        var search = await App.OpenPaletteAsync();
        await Expect(ExactOption("New annotation")).ToBeVisibleAsync();
        await Expect(ExactOption("Bulk create annotations")).ToBeVisibleAsync();

        await search.FillAsync("Schemas");
        await Expect(ExactOption("SchemasModeling")).ToBeVisibleAsync();

        // The filter applies about 100 ms after the input: once the Schemas option has gone, the result is final.
        await search.FillAsync("Export & Import");
        await Expect(App.PaletteOption("Schemas")).ToHaveCountAsync(0);
        await Expect(App.PaletteOption("Export & Import")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(App.CommandPalette).ToBeHiddenAsync();
    }

    // NAV-MNU-05: A Reader's menu locks the editing sections.
    // A Reader of the project finds Annotations, Configurations, Values store and Modeling locked (the icon rail's
    // tooltips say why) and Graph open, only Views and Settings of the project, and a palette without the editor
    // actions and without Modeling's tabs.
    [Test]
    [TestCase(30)]
    [UserRole(Role.Reader)]
    public async Task Navigation_ReaderMenu_LocksEditingSections()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.WaitForCatalogAsync();

        var menu = await App.MenuAsync();
        foreach (var path in new[] { Routes.Annotations, Routes.Configurations, Routes.KeyValues, Routes.Modeling })
        {
            await Expect(App.NavigationMenuLink(menu, path).Locator(LockBadge)).ToHaveCountAsync(1);
        }

        await Expect(App.NavigationMenuLink(menu, Routes.Graph)).ToHaveCountAsync(1);
        await Expect(App.NavigationMenuLink(menu, Routes.Graph).Locator(LockBadge)).ToHaveCountAsync(0);

        // The project's links outside the view: only Views and Settings.
        await Expect(menu.Locator($"a[href^=\"/{Routes.ProjectRoot}/\"]:not([href^=\"/{Routes.ViewRoot}/\"])")).ToHaveCountAsync(2);
        await Expect(App.NavigationMenuLink(menu, Routes.Views)).ToHaveCountAsync(1);
        await Expect(App.NavigationMenuLink(menu, Routes.Settings())).ToHaveCountAsync(1);
        foreach (var path in new[] { Routes.Members, Routes.Machines, Routes.Monitor() })
        {
            await Expect(App.NavigationMenuLink(menu, path)).ToHaveCountAsync(0);
        }

        await App.CloseMenuAsync();

        // The lock's tooltip shows only in the icon rail and only on a mouse hover, which a phone does not have.
        if (!App.IsPhone)
        {
            await Page.SetViewportSizeAsync(1100, 900);
            await Expect(App.ExpandSidebarButton).ToBeVisibleAsync();
            var rail = App.DockedNavigationMenu;

            var annotationsEntry = App.NavigationMenuLink(rail, Routes.Annotations);
            await annotationsEntry.HoverAsync();
            await Expect(annotationsEntry).ToHaveAccessibleDescriptionAsync("Read-only for your role");

            // The tooltip closes once the mouse leaves its entry, before the next one opens.
            await Page.Mouse.MoveAsync(700, 450);
            var modelingEntry = App.NavigationMenuLink(rail, Routes.Modeling);
            await modelingEntry.HoverAsync();
            await Expect(modelingEntry).ToHaveAccessibleDescriptionAsync("Contributors only");
            await Page.Mouse.MoveAsync(700, 450);
        }

        var search = await App.OpenPaletteAsync();
        await Expect(ExactOption("Switch project")).ToBeVisibleAsync();
        await Expect(ExactOption("New annotation")).ToHaveCountAsync(0);
        await Expect(ExactOption("Bulk create annotations")).ToHaveCountAsync(0);

        await search.FillAsync("Modeling");
        await Expect(ExactOption($"ModelingView · {Routes.View}")).ToBeVisibleAsync();

        // A locked Modeling lists no tabs: once the Modeling option has gone, the result is final.
        await search.FillAsync("Schemas");
        await Expect(App.PaletteOption("Modeling")).ToHaveCountAsync(0);
        await Expect(App.PaletteOption("Schemas")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(App.CommandPalette).ToBeHiddenAsync();
    }

    // NAV-MNU-06: Unpinning the sidebar moves the menu behind Open navigation and is kept.
    // On a desktop, "Unpin sidebar" replaces the docked sidebar with the slideover "Navigation" behind "Open navigation",
    // whose links navigate and close it; the browser keeps the choice over a reload, and the palette pins it again.
    [Test]
    [TestCase(33)]
    [DesktopOnly]
    public async Task Sidebar_Unpin_MovesMenuBehindButtonAndIsKept()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await Expect(App.Sidebar).ToBeVisibleAsync();
        await Expect(App.UnpinSidebarButton).ToBeVisibleAsync();
        await Expect(App.OpenNavigationButton).ToBeHiddenAsync();

        await App.UnpinSidebarButton.ClickAsync();
        await Expect(App.Sidebar).ToHaveCountAsync(0);
        await Expect(App.OpenNavigationButton).ToBeVisibleAsync();

        await App.OpenNavigationButton.ClickAsync();
        var navigation = App.UnpinnedNavigationDialog;
        await Expect(navigation).ToBeVisibleAsync();
        await Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true })).ToBeVisibleAsync();
        await Expect(navigation.GetByRole(AriaRole.Button, new() { Name = "Pin sidebar", Exact = true })).ToBeVisibleAsync();

        // Not App.NavigateAsync: at this width it would look for the docked sidebar.
        await navigation.GetByRole(AriaRole.Link, new() { Name = "Configurations", Exact = true }).ClickAsync();
        await App.ExpectAtAsync(Routes.Configurations);
        await Expect(navigation).ToBeHiddenAsync();

        await App.GotoAsync(Routes.Configurations);
        await App.WaitForReadyAsync();
        await Expect(App.OpenNavigationButton).ToBeVisibleAsync();
        await Expect(App.Sidebar).ToHaveCountAsync(0);

        await App.RunCommandAsync("Pin the sidebar");
        await Expect(App.Sidebar).ToBeVisibleAsync();
        await Expect(App.UnpinSidebarButton).ToBeVisibleAsync();
        await Expect(App.OpenNavigationButton).ToBeHiddenAsync();
    }

    // NAV-MNU-07: The icon rail expands and collapses, and [ toggles the docked sidebar.
    // Between 1024 and 1279 px the docked sidebar starts as the icon rail, whose links have no names; "Expand sidebar"
    // and "Collapse sidebar" switch it, widening the window expands it, and "[" (with no text box focused) toggles it.
    [Test]
    [TestCase(35)]
    [DesktopOnly]
    public async Task Sidebar_IconRail_ExpandsCollapsesAndToggles()
    {
        await Page.SetViewportSizeAsync(1100, 900);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var configurationsByName = Page.GetByRole(AriaRole.Link, new() { Name = "Configurations", Exact = true });
        await Expect(App.ExpandSidebarButton).ToBeVisibleAsync();
        await Expect(App.NavigationMenuLink(App.DockedNavigationMenu, Routes.Configurations)).ToBeVisibleAsync();
        await Expect(configurationsByName).ToHaveCountAsync(0);

        await App.ExpandSidebarButton.ClickAsync();
        await Expect(configurationsByName).ToBeVisibleAsync();
        await Expect(App.CollapseSidebarButton).ToBeVisibleAsync();

        await App.CollapseSidebarButton.ClickAsync();
        await Expect(App.ExpandSidebarButton).ToBeVisibleAsync();
        await Expect(configurationsByName).ToHaveCountAsync(0);

        await Page.SetViewportSizeAsync(1440, 900);
        await Expect(App.CollapseSidebarButton).ToBeVisibleAsync();
        await Expect(configurationsByName).ToBeVisibleAsync();

        await App.PressShortcutAsync("[");
        await Expect(App.ExpandSidebarButton).ToBeVisibleAsync();
        await Expect(configurationsByName).ToHaveCountAsync(0);

        await App.PressShortcutAsync("[");
        await Expect(App.CollapseSidebarButton).ToBeVisibleAsync();
        await Expect(configurationsByName).ToBeVisibleAsync();
    }

    // The entries of the menu's view section, in their order: the links into the view of the routes.
    private ILocator ViewEntries(ILocator menu)
    {
        return menu.Locator($"a[href^=\"/{Routes.ViewRoot}/\"]");
    }

    // An option of the open command palette by its whole name (the label and its suffix: "SchemasModeling").
    private ILocator ExactOption(string name)
    {
        return App.CommandPalette.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
    }
}
