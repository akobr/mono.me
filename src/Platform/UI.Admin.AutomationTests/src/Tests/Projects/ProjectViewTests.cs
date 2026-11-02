using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Projects;

// Views: the Views page of a project: New view in a disposable project, the href of the Compare link and opening a
// view from the list, registering the discovered default view and editing its description, the validation of New
// view, and the actions that Readers and Contributors see.
[Area(Area.Projects)]
public sealed partial class ProjectViewTests : PlatformTest
{
    private const string PatternMessage = "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit.";

    // ORG-VEW-01: New view creates a view that Compare links to and the list opens.
    // In a new project the default view is the only one, so its Compare is disabled and its tooltip says there is
    // nothing to compare with. New view creates a registered view with its description; the default row's tooltip then
    // says the other views are compared with it, the new view's Compare links to Compare views of the default view
    // with ?to=<view> (opening it belongs to Modeling, MOD-CMP-01), and the view's link opens its empty annotations.
    [Test]
    [TestCase(31)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Views_Create_LinksCompareAndOpensView()
    {
        var project = await CreateDisposableProjectAsync();
        var view = PlatformData.ViewName();
        var description = PlatformData.Description(120);

        var views = new ViewsPage(App, project);
        await views.OpenAsync();
        var defaultCompare = views.ViewCompareLink(AppRoutes.DefaultView);
        await Expect(views.ViewProductionBadge(AppRoutes.DefaultView)).ToBeVisibleAsync();
        await Expect(defaultCompare).ToBeDisabledAsync();
        await Expect(defaultCompare).Not.ToHaveAttributeAsync("href", AnyValue());
        await Expect(views.NewViewButton).ToBeVisibleAsync();

        if (!App.IsPhone)
        {
            await views.HoverCompareAsync(AppRoutes.DefaultView);
            await Expect(defaultCompare).ToHaveAccessibleDescriptionAsync(ViewsPage.NoOtherViewHint);
            await views.LeaveCompareAsync(AppRoutes.DefaultView);
        }

        await views.NewViewButton.ClickAsync();
        await Expect(views.NewViewDialog).ToBeVisibleAsync();
        await Expect(views.NewViewMessage("A view is a separate catalog of annotations and configurations in the project.")).ToBeVisibleAsync();
        await Expect(views.NewViewName).ToBeVisibleAsync();
        await Expect(views.NewViewDescription).ToBeVisibleAsync();

        await views.NewViewName.FillAsync(view);
        await views.NewViewDescription.FillAsync(description);
        await views.CreateViewButton.ClickAsync();
        await Expect(App.Toast($"View {view} created")).ToBeVisibleAsync();
        await Expect(views.NewViewDialog).ToBeHiddenAsync();
        await Expect(views.ViewRow(view)).ToContainTextAsync(description);
        await Expect(views.ViewEditButton(view)).ToBeVisibleAsync();
        await Expect(views.ViewProductionBadge(view)).ToHaveCountAsync(0);

        // The hint follows the list, which is fetched again after the toast; Expect waits for it.
        if (!App.IsPhone)
        {
            await views.HoverCompareAsync(AppRoutes.DefaultView);
            await Expect(defaultCompare).ToBeDisabledAsync();
            await Expect(defaultCompare).ToHaveAccessibleDescriptionAsync(ViewsPage.OtherViewsHint);
            await views.LeaveCompareAsync(AppRoutes.DefaultView);
        }

        // Only the href: Compare views itself belongs to Modeling.
        await Expect(views.ViewCompareLink(view)).ToBeEnabledAsync();
        await Expect(views.ViewCompareLink(view)).ToHaveAttributeAsync("href", CompareHref(project, view));

        var created = project with { View = view };
        await views.View(view).ClickAsync();
        await App.ExpectAtAsync(created.Annotations);
        await Expect(new AnnotationsPage(App, created).EmptyState).ToBeVisibleAsync();
    }

    // ORG-VEW-02: Register the discovered default view and edit its description.
    // A new project's default view is not registered (on the mock and on the real API), so it is listed as
    // "discovered" with Register. Register makes it a registered view with Edit; Edit saves a description, which stays
    // after a reload, and saving an empty description removes it again.
    [Test]
    [TestCase(34)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Views_RegisterDefault_EditsDescription()
    {
        const string view = AppRoutes.DefaultView;
        var project = await CreateDisposableProjectAsync();
        var description = PlatformData.Description(120);

        var views = new ViewsPage(App, project);
        await views.OpenAsync();
        await Expect(views.ViewProductionBadge(view)).ToBeVisibleAsync();
        await Expect(views.ViewDiscoveredBadge(view)).ToBeVisibleAsync();
        await Expect(views.ViewRegisterButton(view)).ToBeVisibleAsync();
        await Expect(views.ViewEditButton(view)).ToHaveCountAsync(0);

        await views.ViewRegisterButton(view).ClickAsync();
        await Expect(App.Toast($"View {view} registered")).ToBeVisibleAsync();
        await Expect(views.ViewDiscoveredBadge(view)).ToHaveCountAsync(0);
        await Expect(views.ViewEditButton(view)).ToBeVisibleAsync();

        await views.ViewEditButton(view).ClickAsync();
        await Expect(views.EditViewDialog(view)).ToBeVisibleAsync();
        await Expect(views.EditViewDescription(view)).ToBeVisibleAsync();
        await views.EditViewDescription(view).FillAsync(description);
        await views.EditViewSaveButton(view).ClickAsync();
        await Expect(App.Toast("Description saved")).ToBeVisibleAsync();
        await Expect(views.EditViewDialog(view)).ToBeHiddenAsync();
        await Expect(views.ViewRow(view)).ToContainTextAsync(description);

        // The reload also ends the first "Description saved", so the toast of the second save is its own.
        await Page.ReloadAsync();
        await App.WaitForReadyAsync();
        await Expect(views.ViewRow(view)).ToContainTextAsync(description);
        await Expect(views.ViewEditButton(view)).ToBeVisibleAsync();
        await Expect(views.ViewDiscoveredBadge(view)).ToHaveCountAsync(0);

        // The API stores no description, the mock an empty one: either way the row no longer shows it.
        await views.SaveViewDescriptionAsync(view, string.Empty);
        await Expect(App.Toast("Description saved")).ToBeVisibleAsync();
        await Expect(views.ViewRow(view)).Not.ToContainTextAsync(description);
    }

    // ORG-VEW-03: New view validates the name and rejects an existing view.
    // The client shows the first error under Name after each submit: the empty name, a name against the pattern and a
    // reserved view name. "default" passes the client, and the API refuses it as an existing view, shown under Name
    // without a toast. Nothing is created, so the target project lists the same views after Cancel.
    [Test]
    [TestCase(36)]
    [UserRole(Role.Admin)]
    public async Task Views_CreateInvalid_ShowsNameErrors()
    {
        var views = new ViewsPage(App, Routes);
        await views.OpenAsync();
        await Expect(views.ViewProductionBadge(AppRoutes.DefaultView)).ToBeVisibleAsync();
        await Expect(views.NewViewButton).ToBeVisibleAsync();
        var listed = await views.ListedAsync();

        await views.NewViewButton.ClickAsync();
        await Expect(views.NewViewDialog).ToBeVisibleAsync();
        await views.CreateViewButton.ClickAsync();
        await Expect(views.NewViewName).ToHaveAccessibleDescriptionAsync("Enter a view name.");

        await views.NewViewName.FillAsync("Bad Name");
        await views.CreateViewButton.ClickAsync();
        await Expect(views.NewViewName).ToHaveAccessibleDescriptionAsync(PatternMessage);

        await views.NewViewName.FillAsync("members");
        await views.CreateViewButton.ClickAsync();
        await Expect(views.NewViewName).ToHaveAccessibleDescriptionAsync("This view name is reserved.");

        // The default view always exists: the API answers 409 ViewExists.
        await views.NewViewName.FillAsync(AppRoutes.DefaultView);
        await views.CreateViewButton.ClickAsync();
        await Expect(views.NewViewName).ToHaveAccessibleDescriptionAsync("A view with this name already exists.");
        await Expect(App.Notifications.GetByText(ViewCreatedToast())).ToHaveCountAsync(0);
        await Expect(views.NewViewDialog).ToBeVisibleAsync();

        await views.NewViewCancelButton.ClickAsync();
        await Expect(views.NewViewDialog).ToBeHiddenAsync();
        await Expect(views.View(AppRoutes.DefaultView)).ToBeVisibleAsync();
        await Assert.That(string.Join(", ", await views.ListedAsync())).IsEqualTo(string.Join(", ", listed));
    }

    // ORG-VEW-04: A Reader lists the views without Compare or managing actions.
    // Compare views is a tab of Modeling, which a Reader cannot open, and only Administrators manage views: a Reader
    // sees the list without New view, Compare, Register or Edit, and a view's link still opens its annotations.
    [Test]
    [TestCase(37)]
    [UserRole(Role.Reader)]
    public async Task Views_Reader_ListsWithoutActions()
    {
        var views = new ViewsPage(App, Routes);
        await views.OpenAsync();
        await Expect(views.ViewProductionBadge(AppRoutes.DefaultView)).ToBeVisibleAsync();

        await Expect(views.NewViewButton).ToHaveCountAsync(0);
        await Expect(views.CompareLinks).ToHaveCountAsync(0);
        await Expect(views.RegisterButtons).ToHaveCountAsync(0);
        await Expect(views.EditButtons).ToHaveCountAsync(0);

        await views.View(AppRoutes.DefaultView).ClickAsync();
        await App.ExpectAtAsync((Routes with { View = AppRoutes.DefaultView }).Annotations);
    }

    // ORG-VEW-05: A Contributor compares views but cannot create, register or edit them.
    // A Contributor gets Compare in every row (disabled in the default view's own) and no New view, Register or Edit.
    // Every other view's Compare links to Compare views of the default view with ?to=<view>, and the default row's
    // tooltip follows whether another view is listed (the mock lists only default).
    [Test]
    [TestCase(38)]
    [UserRole(Role.Contributor)]
    public async Task Views_Contributor_ComparesWithoutManaging()
    {
        var views = new ViewsPage(App, Routes);
        await views.OpenAsync();
        var defaultCompare = views.ViewCompareLink(AppRoutes.DefaultView);
        await Expect(views.ViewProductionBadge(AppRoutes.DefaultView)).ToBeVisibleAsync();
        await Expect(defaultCompare).ToBeDisabledAsync();
        await Expect(defaultCompare).Not.ToHaveAttributeAsync("href", AnyValue());

        await Expect(views.NewViewButton).ToHaveCountAsync(0);
        await Expect(views.RegisterButtons).ToHaveCountAsync(0);
        await Expect(views.EditButtons).ToHaveCountAsync(0);

        var listed = await views.ListedAsync();
        var project = Routes with { View = AppRoutes.DefaultView };
        foreach (var view in listed.Where(view => !string.Equals(view, AppRoutes.DefaultView, StringComparison.Ordinal)))
        {
            await Expect(views.ViewCompareLink(view)).ToBeEnabledAsync();
            await Expect(views.ViewCompareLink(view)).ToHaveAttributeAsync("href", CompareHref(project, view));
        }

        if (!App.IsPhone)
        {
            await views.HoverCompareAsync(AppRoutes.DefaultView);
            await Expect(defaultCompare).ToHaveAccessibleDescriptionAsync(listed.Count > 1 ? ViewsPage.OtherViewsHint : ViewsPage.NoOtherViewHint);
        }
    }

    // The href of a view's Compare: Compare views of the project's default view with ?to=<view>, at the end of the
    // href (which starts with the app's base).
    private static Regex CompareHref(AppRoutes project, string view)
    {
        return new Regex($"/{Regex.Escape((project with { View = AppRoutes.DefaultView }).Compare(view))}$");
    }

    // Any value: Not.ToHaveAttributeAsync(name, AnyValue()) passes only while the attribute is absent.
    [GeneratedRegex(".*")]
    private static partial Regex AnyValue();

    [GeneratedRegex("^View .+ created$")]
    private static partial Regex ViewCreatedToast();
}
