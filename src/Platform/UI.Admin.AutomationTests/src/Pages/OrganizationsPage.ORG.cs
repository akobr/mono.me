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
// The offer of the exhibition (ExhibitionOfferCard.vue, 424-271): for a user with an account and no role in the
// deployment's exhibition, the last card of the grid (below the empty state without organizations) has the heading
// "The exhibition", the wizard's two sentences about it (OnboardingPage.ExhibitionText and
// OnboardingPage.ExhibitionChoiceDescriptionText) and the button "Join the exhibition", a button and not a link, so
// ListedAsync lists no project for it. The page asks for the exhibition once per page load; a deployment without one
// (or any failure of the request) shows no card.
public sealed partial class OrganizationsPage
{
    // The heading (h2) of the offer of the exhibition.
    public const string ExhibitionOfferHeadingText = "The exhibition";

    // The button of the offer of the exhibition.
    public const string JoinExhibitionText = "Join the exhibition";

    // The toast of a join, the same as the onboarding wizard's.
    public const string JoinedExhibitionToast = "You joined the exhibition";

    public ILocator NewOrganizationDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New organization", Exact = true });

    // The card of the offer of the exhibition (data-exhibition-offer); none while nothing is offered.
    public ILocator ExhibitionOffer => app.Page.Locator("[data-exhibition-offer]");

    // The heading of the offer and its button, which nothing else of the page has: none while nothing is offered.
    public ILocator ExhibitionOfferHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = ExhibitionOfferHeadingText, Exact = true, Level = 2 });

    public ILocator JoinExhibitionButton => app.Page.GetByRole(AriaRole.Button, new() { Name = JoinExhibitionText, Exact = true });

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

    // A text of the offer of the exhibition by its exact words, e.g. OnboardingPage.ExhibitionText.
    public ILocator ExhibitionOfferText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return ExhibitionOffer.GetByText(text, new() { Exact = true });
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

    // Clicks "Join the exhibition" and returns once the app left /orgs for the exhibition's annotations, i.e. the join
    // succeeded (with the toast JoinedExhibitionToast). A refused join keeps the page, with a toast that says why ("The
    // exhibition is no longer offered.", "Joining the exhibition failed"), so this then fails with the page still at
    // /orgs.
    public async Task JoinExhibitionAsync()
    {
        await JoinExhibitionButton.ClickAsync();
        await Expect(app.Page).Not.ToHaveURLAsync(OrganizationsUrl());
    }

    [GeneratedRegex("^Organization")]
    private static partial Regex OrganizationLabel();

    [GeneratedRegex("^First project")]
    private static partial Regex FirstProjectLabel();

    // The URL of /orgs, whatever its query and hash.
    [GeneratedRegex(@"/orgs/?(?:[?#].*)?$")]
    private static partial Regex OrganizationsUrl();
}
