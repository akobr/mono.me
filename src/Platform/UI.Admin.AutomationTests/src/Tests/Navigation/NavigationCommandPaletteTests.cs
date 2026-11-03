using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Command Palette: "Search…" (Ctrl/Meta+K) opens pages and projects, runs the editor actions, finds annotations and
// lists the recent ones, and switches the global theme. An option's name joins its label and its suffix
// ("SettingsProject"), and the palette filters about 100 ms after the input.
[Area(Area.Navigation)]
public sealed partial class NavigationCommandPaletteTests : PlatformTest
{
    // The suffix of a project's option in the palette: the user's role in the project.
    private const string ProjectRoles = "(?:Owner|Administrator|ContributorWithSecrets|Contributor|Reader)";

    // NAV-PAL-01: The command palette opens pages and projects, also from a text box.
    // Off a view page (where no annotation of a live catalog can rank first) the palette lists its groups, opens the
    // remembered project's Settings, and a project with the user's role as its suffix; Ctrl/Meta+K opens it although
    // the filter has the focus, which keeps its value after Escape, and "Switch project" opens the organizations.
    [Test]
    [TestCase(72)]
    public async Task CommandPalette_PagesAndProjects_Navigate()
    {
        var annotations = new AnnotationsPage(App, Routes);
        var defaultView = Routes with { View = AppRoutes.DefaultView };
        await annotations.OpenAsync();
        await App.NavigateAsync("Organizations");
        await App.ExpectAtAsync(Routes.Organizations);

        var search = await App.OpenPaletteAsync();
        foreach (var group in new[] { "Pages", "Projects", "Actions", "Theme" })
        {
            await Expect(PaletteGroup(group)).ToBeVisibleAsync();
        }

        await search.FillAsync("Settings");
        await App.PickFirstPaletteOptionAsync("SettingsProject");
        await App.ExpectAtAsync(Routes.Settings());

        await Page.Keyboard.PressAsync("ControlOrMeta+K");
        await Expect(App.CommandInput).ToBeVisibleAsync();
        var project = $"{Routes.Organization} / {Routes.Project}";
        await App.CommandInput.FillAsync(project);
        await App.PickFirstPaletteOptionAsync(new Regex($"^{Regex.Escape(project)}{ProjectRoles}$"));
        await App.ExpectAtAsync(defaultView.Annotations);

        // A value that matches nothing, so the check that the filter keeps it can fail.
        var filter = $"key:{PlatformData.Slug("pal")}";
        await annotations.FilterAsync(filter);
        await annotations.Filter.ClickAsync();
        await Expect(annotations.Filter).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+K");
        await Expect(App.CommandInput).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(App.CommandPalette).ToBeHiddenAsync();
        await Expect(annotations.Filter).ToHaveValueAsync(filter);

        await App.NavigateAsync("Settings");
        await App.ExpectAtAsync(Routes.Settings());
        await App.RunCommandAsync("Switch project");
        await App.ExpectAtAsync(Routes.Organizations);
        await Expect(new OrganizationsPage(App, Routes).Heading).ToBeVisibleAsync();
    }

    // NAV-PAL-02: The palette's editor actions create an annotation that the palette then finds and lists as recent.
    // On a view page with its catalog, the palette's Bulk create creates a responsibility and New annotation opens its
    // dialog; the palette then finds the responsibility by its key in "Annotations", opens its detail, and lists it in
    // "Recent" once the detail closed.
    [Test]
    [TestCase(75)]
    [UserRole(Role.Editor)]
    public async Task CommandPalette_EditorActions_FindAndRecallAnnotation()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var configurations = new ConfigurationsPage(App, Routes);
        await configurations.OpenAsync();
        await Expect(configurations.Loading).ToBeHiddenAsync();
        await Expect(configurations.LoadedConfigurations).ToBeVisibleAsync();

        await App.RunCommandAsync("Bulk create annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        var bulkCreate = new BulkCreateDialog(App);
        await Expect(bulkCreate.Root).ToBeVisibleAsync();
        await bulkCreate.EnterKeysAsync(key);
        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
        await bulkCreate.CloseAsync();

        await App.RunCommandAsync("New annotation");
        var newAnnotation = new NewAnnotationDialog(App);
        await Expect(newAnnotation.Root).ToBeVisibleAsync();
        await newAnnotation.CancelAsync();
        await App.ExpectAtAsync(Routes.Annotations);

        // Not RunCommandAsync: the option's name starts with the annotation's name, not with its key.
        var search = await App.OpenPaletteAsync();
        await search.FillAsync(key);
        var found = PaletteGroup("Annotations").GetByRole(AriaRole.Option).Filter(new() { HasText = key });
        await Expect(found).ToBeVisibleAsync();
        await found.ClickAsync();
        await Expect(App.CommandPalette).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        var detail = new AnnotationDetail(App, Routes);
        await Expect(detail.Identity).ToHaveTextAsync(key);

        await detail.CloseAsync();
        await App.OpenPaletteAsync();
        await Expect(PaletteGroup("Recent").GetByRole(AriaRole.Option).Filter(new() { HasText = key })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(App.CommandPalette).ToBeHiddenAsync();
    }

    // NAV-PAL-03: The theme switches in the palette and on the Account page and is kept.
    // With a light browser scheme, the palette's Theme group switches the app to Dark, which a reload keeps and the
    // Account page shows; there Light and System switch it back, and System follows the browser's scheme. The top bar
    // has no theme button of its own.
    [Test]
    [TestCase(76)]
    public async Task Theme_SwitchInPaletteAndAccount_IsKept()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        var html = Page.Locator("html");
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await Expect(html).Not.ToHaveClassAsync(DarkClass());
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Color mode", Exact = true })).ToHaveCountAsync(0);

        // An empty search: typed, "Dark" could rank a live annotation first on a view page.
        await App.OpenPaletteAsync();
        var theme = PaletteGroup("Theme");
        foreach (var option in new[] { "System", "Light", "Dark" })
        {
            await Expect(theme.GetByRole(AriaRole.Option, new() { Name = option, Exact = true })).ToBeVisibleAsync();
        }

        await theme.GetByRole(AriaRole.Option, new() { Name = "Dark", Exact = true }).ClickAsync();
        await Expect(App.CommandPalette).ToBeHiddenAsync();
        await Expect(html).ToHaveClassAsync(DarkClass());

        await App.GotoAsync(Routes.Annotations);
        await App.WaitForReadyAsync();
        await Expect(html).ToHaveClassAsync(DarkClass());

        await App.OpenUserMenuAsync();
        await App.UserMenuItem("Account").ClickAsync();
        await App.ExpectAtAsync(Routes.Account);
        var account = new AccountPage(App, Routes);
        await Expect(account.ThemeTab("Dark")).ToHaveAttributeAsync("aria-selected", "true");

        await account.SetThemeAsync("Light");
        await Expect(html).Not.ToHaveClassAsync(DarkClass());

        await account.SetThemeAsync("System");
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Expect(html).ToHaveClassAsync(DarkClass());
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(html).Not.ToHaveClassAsync(DarkClass());
    }

    // The dark theme's class of <html>.
    [GeneratedRegex(@"\bdark\b")]
    private static partial Regex DarkClass();

    // A group of the open command palette by its label ("Recent", "Annotations", "Pages", "Projects", "Actions",
    // "Theme"): a listbox group labelled by it.
    private ILocator PaletteGroup(string label)
    {
        return App.CommandPalette.GetByRole(AriaRole.Group, new() { Name = label, Exact = true });
    }
}
