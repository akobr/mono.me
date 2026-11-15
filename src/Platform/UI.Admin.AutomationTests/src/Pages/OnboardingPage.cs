using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /onboarding (bare layout, no user menu): where the auth guard sends a signed-in user without an account and without
// a pending invitation. "Welcome, <name>" above the card of a wizard: Welcome (where customer data lives, and the
// sub-processors), Security (how machines and people sign in), Exhibition (the choice to join the deployment's demo
// project as a Reader; a deployment without an exhibition has no such step) and Organization. The step list names its
// buttons "Step <n>: <name>" in every layout (a phone shows only the name of the current one) and marks the current
// step; every step has an h2. The step and the choice are in the address (?step=security, exhibition or organization,
// none for Welcome, and join=1), so OpenAsync opens a step, and the browser's back and a reload keep both. "Sign out"
// is below the card on every step.
// The last step is the organization form (OrganizationForm.vue): "Organization" and "First project" (required, so
// their accessible names end in '*'), "Project description" (sent trimmed), and "Create and continue", which creates
// the account with the organization and its first project and opens the project's annotations. The form shows the
// first error of each field under it ("Enter a organization name.", sic). With the exhibition chosen the step says so
// and has "Continue with the exhibition only", which joins without an organization and opens the exhibition. The step
// stays in the page while another one shows, so it keeps what was typed: its heading, fields and buttons are then
// hidden, not gone (ToBeHiddenAsync, not a count of 0).
public sealed partial class OnboardingPage(App app, AppRoutes routes)
{
    // The heading of the last step, the organization form.
    public const string OrganizationHeading = "Create your organization";

    // The bold sentence of the callout of the first step, under its heading.
    public const string HostingLeadText = "All customer data, configuration and secrets are stored and processed only in the EU (Sweden).";

    // The paragraph under the callout, in the form the admin UI shows until the three safeguards run in production
    // (blocked impersonation, the audit log of access changes, a recent sign-in for secrets): without their sentence.
    public const string HostingStatementText = "WorkOS (USA) is used only to sign in administrators of the 2S Admin UI and to send their invitation emails. It processes only their name, email, IP address and session data, and the email address of a person they invite. Every authorization decision is made by 2S in the EU. Customers that need an EU-only identity provider can use Entra ID or Keycloak.";

    // The paragraph of "Machines and pipelines" on the security step.
    public const string MachinesText = "They need no stored secret. A machine exchanges its own platform token, for example a GitHub Actions OIDC token or a Kubernetes service account token, for a short-lived 2S token.";

    // The paragraph of "People" above its list of identity providers.
    public const string PeopleText = "This platform signs you in with WorkOS AuthKit. A dedicated deployment can sign people in with an identity provider of your own instead:";

    // The paragraph of "People" under the list: what such a deployment gives up.
    public const string PeopleLimitText = "Sign-in is then fully under your control, and WorkOS is not involved. Such a deployment is used through sform and the API; the Admin UI is not available in it today.";

    public const string IntroText = "Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.";

    public const string JoiningExhibitionText = "You are joining the exhibition.";

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = WelcomeTitle(), Level = 1 });

    // The list of the steps, above the step that shows. The wizard shows it once it knows whether the deployment has an
    // exhibition, so its buttons never change under the user; the mock has started by then.
    public ILocator StepList => app.Page.GetByRole(AriaRole.Navigation, new() { Name = "Onboarding steps", Exact = true });

    // The buttons of the step list in their order: four, or three without an exhibition.
    public ILocator Steps => StepList.GetByRole(AriaRole.Button);

    public ILocator CurrentStep => StepList.Locator("[aria-current='step']");

    // On every step but the last, where the form has "Create and continue" instead.
    public ILocator NextButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true });

    // On every step but the first; the one of the form is in the page from the start, hidden with its step.
    public ILocator BackButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true });

    // A button styled as a link, below the card.
    public ILocator SignOutButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true });

    public ILocator HostingLead => Text(HostingLeadText);

    public ILocator HostingStatement => Text(HostingStatementText);

    // The entries of the list "Sub-processors" of the first step, in their order; each holds its name, a badge with its
    // region ("EU only" or "USA") and its purpose, three elements of their own.
    public ILocator SubProcessors => app.Page.GetByRole(AriaRole.List, new() { Name = "Sub-processors", Exact = true }).GetByRole(AriaRole.Listitem);

    // The identity providers of a dedicated deployment, the list of "People" on the security step.
    public ILocator IdentityProviders => app.Page.Locator("section[data-step='security']").GetByRole(AriaRole.Listitem);

    // The links of the step that shows; each opens in a new tab.
    public ILocator StepLinks => StepSections.GetByRole(AriaRole.Link);

    // A Nuxt UI checkbox, a button with the checkbox role.
    public ILocator JoinExhibitionCheckbox => app.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Join the exhibition", Exact = true });

    public ILocator Intro => Text(IntroText);

    public ILocator OrganizationField => app.Page.GetByRole(AriaRole.Textbox, new() { NameRegex = OrganizationLabel() });

    public ILocator ProjectField => app.Page.GetByRole(AriaRole.Textbox, new() { NameRegex = ProjectLabel() });

    public ILocator DescriptionField => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Project description", Exact = true });

    public ILocator CreateAndContinueButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Create and continue", Exact = true });

    // Under the form of the last step, only with the exhibition chosen.
    public ILocator JoiningExhibition => Text(JoiningExhibitionText);

    // Next to JoiningExhibition: the second way to end the wizard, which creates no organization.
    public ILocator ExhibitionOnlyButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Continue with the exhibition only", Exact = true });

    // The steps in the page: the one that shows, and the organization step, hidden while another one shows. A locator
    // by role finds nothing in a hidden step.
    private ILocator StepSections => app.Page.Locator("section[data-step]");

    // A button of the step list by its accessible name, "Step <n>: <name>" ("Step 3: Organization" without an
    // exhibition).
    public ILocator Step(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return StepList.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // The heading of a step (h2): "Customer data stays in the EU", "Security first", "Join the exhibition" or
    // OrganizationHeading. Only the step that shows has a visible one.
    public ILocator StepHeading(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true, Level = 2 });
    }

    // A heading inside a step (h3): "Sub-processors", "Machines and pipelines", "People".
    public ILocator Topic(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true, Level = 3 });
    }

    // A text of the wizard by its whole text: a paragraph of a step, the description of the checkbox.
    public ILocator Text(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return app.Page.GetByText(text, new() { Exact = true });
    }

    // A link of the step that shows by its name, e.g. "Details of the EU hosting".
    public ILocator Link(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return StepSections.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    }

    // What checking the box of the exhibition step does, under its label; the names are those of the exhibition's
    // organization and project.
    public ILocator ExhibitionChoiceDescription(string organization, string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        return Text($"Adds you to {organization}/{project} as a Reader. Its administrators see your name and email in the list of members; other visitors do not.");
    }

    // A field error of the form by its exact text.
    public ILocator FieldError(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return app.Page.GetByText(text, new() { Exact = true });
    }

    // Opens the wizard on a step by its address: the first step without one, else "security", "exhibition" or
    // "organization" (an unknown one, or the exhibition of a deployment without one, shows the first step).
    public async Task OpenAsync(string? step = null)
    {
        await app.GotoAsync(AppRoutes.WithQuery(routes.Onboarding, ("step", step)));
        await WaitForAsync();
    }

    // Waits until the auth guard opened the page (the bare layout has no user menu to wait for) and the wizard shows
    // its steps.
    public async Task WaitForAsync()
    {
        await app.ExpectAtAsync(routes.Onboarding);
        await Expect(Heading).ToBeVisibleAsync();
        await Expect(StepList).ToBeVisibleAsync();
    }

    // Asserts the buttons of the step list: exactly these accessible names, in this order.
    public async Task ExpectStepsAsync(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        await Expect(Steps).ToHaveCountAsync(names.Length);
        for (var index = 0; index < names.Length; index++)
        {
            await Expect(Steps.Nth(index)).ToHaveAccessibleNameAsync(names[index]);
        }
    }

    // Clicks Next and returns once the step it opens shows its heading: a second click before the first one arrived
    // would open the same step again.
    public async Task NextAsync(string heading)
    {
        await NextButton.ClickAsync();
        await Expect(StepHeading(heading)).ToBeVisibleAsync();
    }

    // Clicks Back (the card's, or the form's on the last step) and returns once the step it opens shows its heading.
    public async Task BackAsync(string heading)
    {
        await BackButton.ClickAsync();
        await Expect(StepHeading(heading)).ToBeVisibleAsync();
    }

    // Opens a step from the step list, where every step is reachable at any time, by the name of its button, and
    // returns once the step shows its heading.
    public async Task SelectStepAsync(string name, string heading)
    {
        await Step(name).ClickAsync();
        await Expect(StepHeading(heading)).ToBeVisibleAsync();
    }

    // Checks the box of the exhibition step. The choice goes through the address (join=1), so the box follows a moment
    // after the click: a click and a wait, not CheckAsync, which expects the box checked right after its click.
    public async Task ChooseExhibitionAsync()
    {
        await JoinExhibitionCheckbox.ClickAsync();
        await Expect(JoinExhibitionCheckbox).ToBeCheckedAsync();
    }

    // Opens the last step, the organization form, from the step list unless it shows already (the last button of the
    // list, whatever the number of steps); returns once the step shows its heading.
    public async Task GoToOrganizationStepAsync()
    {
        await Expect(StepList).ToBeVisibleAsync();

        var last = Steps.Last;
        if (await last.GetAttributeAsync("aria-current") is null)
        {
            await last.ClickAsync();
        }

        await Expect(StepHeading(OrganizationHeading)).ToBeVisibleAsync();
    }

    // Clicks "Create and continue" of the last step with the form as it is; the caller checks the field errors, or
    // where it leads.
    public async Task SubmitAsync()
    {
        await CreateAndContinueButton.ClickAsync();
    }

    // Clicks "Continue with the exhibition only" of the last step, there once the exhibition is chosen; the caller
    // checks where it leads.
    public async Task ContinueWithExhibitionOnlyAsync()
    {
        await ExhibitionOnlyButton.ClickAsync();
    }

    // Fills the form of the last step (the description as given, spaces included) and submits it, from whatever step
    // shows; the caller checks where it leads.
    public async Task CreateAsync(string organization, string project, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await GoToOrganizationStepAsync();
        await OrganizationField.FillAsync(organization);
        await ProjectField.FillAsync(project);
        if (description is not null)
        {
            await DescriptionField.FillAsync(description);
        }

        await SubmitAsync();
    }

    [GeneratedRegex("^Welcome")]
    private static partial Regex WelcomeTitle();

    [GeneratedRegex("^Organization")]
    private static partial Regex OrganizationLabel();

    [GeneratedRegex("^First project")]
    private static partial Regex ProjectLabel();
}
