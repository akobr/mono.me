using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Organizations and Projects section's part of the Views page. A view's row is the list item of its link, with
// the badges "production" (the default view) and "discovered" (a view that is not registered), its description and
// its actions: "Compare" for Contributors and up, a link to Compare views of the default view with ?to=<view>, which
// the default view's own row has disabled (aria-disabled, no href) with a tooltip that says whether the project has
// another view; and, for Administrators, "Register" for a discovered view or "Edit" for a registered one. Register
// toasts "View <view> registered"; the dialog "Edit <view>" saves the description with "Description saved"; either
// fetches the list again. The client checks a new view's name like the API's NameRules and shows the first error under
// the field, as its accessible description (the field has no help).
public sealed partial class ViewsPage
{
    public const string NoOtherViewHint = "The project has no other view to compare with";

    public const string OtherViewsHint = "The other views are compared with this one";

    // The list of the views: the ul that holds the default view's link.
    public ILocator ViewList => app.Page.GetByRole(AriaRole.List).Filter(new() { Has = View(AppRoutes.DefaultView) });

    // Every Compare link of the list (Contributors and up).
    public ILocator CompareLinks => ViewList.GetByRole(AriaRole.Link, new() { Name = "Compare", Exact = true });

    // Every Register button of the list (Administrators, discovered views).
    public ILocator RegisterButtons => ViewList.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true });

    // Every Edit button of the list (Administrators, registered views).
    public ILocator EditButtons => ViewList.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true });

    public ILocator NewViewCancelButton => NewViewDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // A text of the dialog "New view" by its exact text: its description, a validation message or the API's conflict.
    public ILocator NewViewMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return NewViewDialog.GetByText(message, new() { Exact = true });
    }

    // The row of a view: the list item that holds its link.
    public ILocator ViewRow(string view)
    {
        return app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = View(view) });
    }

    public ILocator ViewProductionBadge(string view)
    {
        return ViewRow(view).GetByText("production", new() { Exact = true });
    }

    public ILocator ViewDiscoveredBadge(string view)
    {
        return ViewRow(view).GetByText("discovered", new() { Exact = true });
    }

    // Nuxt UI renders the disabled Compare as a link with aria-disabled and without href, so the role finds it too.
    public ILocator ViewCompareLink(string view)
    {
        return ViewRow(view).GetByRole(AriaRole.Link, new() { Name = "Compare", Exact = true });
    }

    public ILocator ViewRegisterButton(string view)
    {
        return ViewRow(view).GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true });
    }

    public ILocator ViewEditButton(string view)
    {
        return ViewRow(view).GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true });
    }

    public ILocator EditViewDialog(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return app.Page.GetByRole(AriaRole.Dialog, new() { Name = $"Edit {view}", Exact = true });
    }

    public ILocator EditViewDescription(string view)
    {
        return EditViewDialog(view).GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });
    }

    public ILocator EditViewSaveButton(string view)
    {
        return EditViewDialog(view).GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
    }

    // The href of a view's Compare: Compare views of the project's default view with ?to=<view>, at the end of the href
    // (which starts with the app's base).
    public Regex CompareHref(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return AppRoutes.HrefEndingWith((routes with { View = AppRoutes.DefaultView }).Compare(view));
    }

    // The view's Compare is disabled: aria-disabled and without href (the default view's own row).
    public async Task ExpectCompareDisabledAsync(string view)
    {
        await Expect(ViewCompareLink(view)).ToBeDisabledAsync();
        await Expect(ViewCompareLink(view)).Not.ToHaveAttributeAsync("href", AnyValue());
    }

    // Hovers the view's Compare link. Only the default row's Compare has a tooltip (whether the project has another
    // view); its node is aria-hidden, so its text is the link's accessible description while it is open. Needs a
    // pointer: not on a phone.
    public async Task HoverCompareAsync(string view)
    {
        await ViewCompareLink(view).HoverAsync();
    }

    // Moves the pointer off the page's content and waits until the tooltip of the view's Compare link closed, so that
    // the next hover opens it again.
    public async Task LeaveCompareAsync(string view)
    {
        await app.Page.Mouse.MoveAsync(0, 0);
        await Expect(ViewCompareLink(view)).ToHaveAccessibleDescriptionAsync(string.Empty);
    }

    // Opens "Edit <view>", replaces the description (empty clears it) and saves; returns once the dialog closed (the
    // toast "Description saved" shows and the list is fetched again).
    public async Task SaveViewDescriptionAsync(string view, string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        await ViewEditButton(view).ClickAsync();
        await Expect(EditViewDialog(view)).ToBeVisibleAsync();
        await EditViewDescription(view).FillAsync(description);
        await EditViewSaveButton(view).ClickAsync();
        await Expect(EditViewDialog(view)).ToBeHiddenAsync();
    }

    // Any value: Not.ToHaveAttributeAsync(name, AnyValue()) passes only while the attribute is absent (Playwright .NET
    // has no overload without a value).
    [GeneratedRegex(".*")]
    private static partial Regex AnyValue();
}
