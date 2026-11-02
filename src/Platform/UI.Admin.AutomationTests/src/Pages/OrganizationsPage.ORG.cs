using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Organizations and Projects section's part of /orgs: the role badge of an organization's card (the user's role
// on the organization, or "Project member" without one) and the dialog "New organization". The dialog
// (OrganizationForm) asks for the organization, its first project and the project's description; the client checks
// the names like the API's NameRules and shows the first error of each field under it, as the field's accessible
// description (it replaces the help). A created organization closes the dialog, toasts "Organization <org> created"
// and opens the project's annotations; a conflict of the API keeps the dialog open with a message under Organization.
public sealed partial class OrganizationsPage
{
    public ILocator NewOrganizationDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New organization", Exact = true });

    // A required field's accessible name ends in '*' ("Organization*").
    public ILocator NewOrganizationName => NewOrganizationDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = OrganizationLabel() });

    public ILocator NewOrganizationFirstProject => NewOrganizationDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = FirstProjectLabel() });

    public ILocator NewOrganizationDescription => NewOrganizationDialog.GetByRole(AriaRole.Textbox, new() { Name = "Project description", Exact = true });

    public ILocator CreateOrganizationButton => NewOrganizationDialog.GetByRole(AriaRole.Button, new() { Name = "Create organization", Exact = true });

    public ILocator NewOrganizationCancelButton => NewOrganizationDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // A message of the dialog by its exact text: a field's validation message or the API's conflict.
    public ILocator NewOrganizationMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return NewOrganizationDialog.GetByText(message, new() { Exact = true });
    }

    // The badge next to the organization's link in its card: the user's role on the organization ("Owner", …), or
    // "Project member" when the user holds only roles of its projects.
    public ILocator OrganizationRoleBadge(string organization, string badge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(badge);
        return Organization(organization).Locator("xpath=..").GetByText(badge, new() { Exact = true });
    }

    // Opens "New organization" (labelled from 640 px, icon-only below) and returns once its dialog shows.
    public async Task OpenNewOrganizationAsync()
    {
        await NewOrganization.ClickAsync();
        await Expect(NewOrganizationDialog).ToBeVisibleAsync();
    }

    // Fills the open dialog and submits it; returns once the dialog closed, i.e. the organization exists (the toast
    // "Organization <org> created" shows and the app opens the project's annotations).
    public async Task SubmitNewOrganizationAsync(string organization, string project, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await NewOrganizationName.FillAsync(organization);
        await NewOrganizationFirstProject.FillAsync(project);
        if (description is not null)
        {
            await NewOrganizationDescription.FillAsync(description);
        }

        await CreateOrganizationButton.ClickAsync();
        await Expect(NewOrganizationDialog).ToBeHiddenAsync();
    }

    [GeneratedRegex("^Organization")]
    private static partial Regex OrganizationLabel();

    [GeneratedRegex("^First project")]
    private static partial Regex FirstProjectLabel();
}
