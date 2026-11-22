using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Access section's part of the project's settings: the setup step of the machine cases in a disposable project,
// one more credential kind in the machine policy, and "Leave project" (ProjectLeave.vue, 424-271) in the footer of the
// Project card, for every member: a text and the button, which opens the confirmation "Leave <org>.<project>?" with the
// role that goes and the way back ("To come back, you need a new invitation.", for the exhibition "You can join the
// exhibition again from Organizations."), "Cancel" and "Leave project". A confirmed leave toasts "You left
// <org>.<project>" and opens /orgs. For the only Owner of the project the button is disabled, and the text says why;
// the page knows that only once it read the project's members (an Administrator's read), so the text comes late.
public sealed partial class ProjectSettingsPage
{
    // The footer's text for a member who may leave.
    public const string LeaveText = "Leave the project to remove it from your organizations.";

    // The footer's text for the only Owner, beside the disabled button.
    public const string OnlyOwnerLeaveText = "You are the only Owner of this project. Make another member an Owner before you leave.";

    // The button in the footer of the Project card and the confirm button of its dialog.
    public const string LeaveProjectLabel = "Leave project";

    public ILocator LeaveProjectButton => ProjectCard.GetByRole(AriaRole.Button, new() { Name = LeaveProjectLabel, Exact = true });

    public ILocator LeaveProjectText => ProjectCard.GetByText(LeaveText, new() { Exact = true });

    public ILocator OnlyOwnerText => ProjectCard.GetByText(OnlyOwnerLeaveText, new() { Exact = true });

    // The confirmation of "Leave project", titled "Leave <organization>.<project>?" for the project of the routes.
    public ConfirmDialog LeaveDialog => new(app, $"Leave {routes.Organization}.{routes.Project}?");

    // Opens the settings, checks the kind (its API name) in "Enabled credential kinds" and saves the machine policy;
    // returns once the toast "Machine policy saved" showed. The policy is project-wide: only in a disposable project.
    public async Task SaveMachinePolicyWithKindAsync(string kind)
    {
        await OpenAsync();
        await EnableCredentialKindAsync(kind);
        await Expect(CredentialKind(kind)).ToBeCheckedAsync();
        await SaveMachinePolicyAsync();
    }

    // Clicks "Leave project" in the footer of the Project card and returns its confirmation once it shows.
    public async Task<ConfirmDialog> OpenLeaveDialogAsync()
    {
        await LeaveProjectButton.ClickAsync();
        var dialog = LeaveDialog;
        await dialog.ExpectOpenAsync();
        return dialog;
    }

    // Confirms the open confirmation of "Leave project" and returns once the dialog closed and the app opened /orgs,
    // i.e. the user left (the toast "You left <org>.<project>"). A refused leave ("You did not leave the project", e.g.
    // for the last Owner) closes the dialog and keeps the page, so this then fails with the page still at Settings.
    public async Task ConfirmLeaveAsync()
    {
        await LeaveDialog.ConfirmAsync(LeaveProjectLabel);
        await app.ExpectAtAsync(routes.Organizations);
    }
}
