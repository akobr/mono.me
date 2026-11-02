using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /onboarding (bare layout, no user menu): where the auth guard sends a signed-in user without an account and without
// a pending invitation. "Welcome, <name>", then the organization form (OrganizationForm.vue): "Organization" and
// "First project" (required, so their accessible names end in '*'), "Project description" (sent trimmed), and "Create
// and continue", which creates the account with the organization and its first project and opens the project's
// annotations. The form shows the first error of each field under it ("Enter a organization name.", sic).
public sealed partial class OnboardingPage(App app, AppRoutes routes)
{
    public const string IntroText = "Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.";

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = WelcomeTitle(), Level = 1 });

    public ILocator Intro => app.Page.GetByText(IntroText, new() { Exact = true });

    public ILocator OrganizationField => app.Page.GetByRole(AriaRole.Textbox, new() { NameRegex = OrganizationLabel() });

    public ILocator ProjectField => app.Page.GetByRole(AriaRole.Textbox, new() { NameRegex = ProjectLabel() });

    public ILocator DescriptionField => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Project description", Exact = true });

    public ILocator CreateAndContinueButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Create and continue", Exact = true });

    // A field error of the form by its exact text.
    public ILocator FieldError(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return app.Page.GetByText(text, new() { Exact = true });
    }

    // Waits until the auth guard opened the page (the bare layout has no user menu to wait for).
    public async Task WaitForAsync()
    {
        await app.ExpectAtAsync(routes.Onboarding);
        await Expect(Heading).ToBeVisibleAsync();
    }

    // Fills the form (the description as given, spaces included) and submits it; the caller checks where it leads.
    public async Task CreateAsync(string organization, string project, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await OrganizationField.FillAsync(organization);
        await ProjectField.FillAsync(project);
        if (description is not null)
        {
            await DescriptionField.FillAsync(description);
        }

        await CreateAndContinueButton.ClickAsync();
    }

    [GeneratedRegex("^Welcome")]
    private static partial Regex WelcomeTitle();

    [GeneratedRegex("^Organization")]
    private static partial Regex OrganizationLabel();

    [GeneratedRegex("^First project")]
    private static partial Regex ProjectLabel();
}
