using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Organizations and Projects section's part of the organization overview: the badge next to the heading (the
// user's role on the organization, or "Member of some projects" without one), the role badge and the key of a
// project's card, the note for a user without an organization role, and the parts of the dialog "New project" its
// validation needs. The client checks the name like the API's NameRules and shows the first error of each field under
// it, as the field's accessible description (it replaces the help); a project that exists keeps the dialog open with
// "A project with this name already exists.". Cancel closes the dialog and keeps what was typed.
public sealed partial class OrganizationOverview
{
    public const string NoOrganizationRoleText =
        "You have no role on the organization itself. An organization role does not grant access to its projects, and project roles do not grant the organization's.";

    // Below the projects, for a user who holds only roles of the organization's projects.
    public ILocator NoOrganizationRoleNote => app.Page.GetByText(NoOrganizationRoleText, new() { Exact = true });

    public ILocator NewProjectCancelButton => NewProjectDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // The badge next to the heading: the user's role on the organization ("Owner", …), or "Member of some projects".
    public ILocator HeadingBadge(string badge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(badge);
        return Heading.Locator("xpath=..").GetByText(badge, new() { Exact = true });
    }

    // The user's role badge on a project's card ("Owner", "Reader", "Contributor + secrets", …).
    public ILocator ProjectCardRoleBadge(string project, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return ProjectCard(project).GetByText(role, new() { Exact = true });
    }

    // The project's key on its card, <org>.<project>.
    public ILocator ProjectCardKey(string project)
    {
        return ProjectCard(project).GetByText($"{routes.Organization}.{project}", new() { Exact = true });
    }

    // A message of the dialog "New project" by its exact text: a field's validation message or the API's conflict.
    public ILocator NewProjectMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return NewProjectDialog.GetByText(message, new() { Exact = true });
    }

    // Opens "New project" and returns once its dialog shows; only an Owner of the organization has it enabled.
    public async Task OpenNewProjectAsync()
    {
        await NewProjectButton.ClickAsync();
        await Expect(NewProjectDialog).ToBeVisibleAsync();
    }
}
