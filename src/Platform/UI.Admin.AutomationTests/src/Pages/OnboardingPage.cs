using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /onboarding (bare layout, no user menu): where the auth guard sends a signed-in user without an account and without
// a pending invitation. "Welcome, <name>" above the card of a wizard: Welcome (the platform, its motto, its pricing and
// the documentation), Residency (where customer data lives, and the sub-processors), Security (secrets, machines,
// people and security testing), Exhibition (the choice to join the deployment's demo project as a Reader; a deployment
// without an exhibition has no such step) and Organization. The step list names its buttons "Step <n>: <name>" in every
// layout (a narrow list shows only the name of the current one) and marks the current step; every step has an h2. The
// step and the choice are in the address (?step=residency, security, exhibition or organization, none for Welcome, and
// join=1), so OpenAsync opens a step, and the browser's back and a reload keep both. "Sign out" is below the card on
// every step.
// The last step is the organization form (OrganizationForm.vue): "Organization" and "First project" (required, so
// their accessible names end in '*'), "Project description" (sent trimmed), and "Create and continue", which creates
// the account with the organization and its first project and opens the project's annotations. Under the form a line
// names the two agreements the account accepts, each a link. The form shows the first error of each field under it
// ("Enter a organization name.", sic). With the exhibition chosen the step says so and has "Continue with the
// exhibition only", which joins without an organization and opens the exhibition. The step stays in the page while
// another one shows, so it keeps what was typed: its heading, fields and buttons are then hidden, not gone
// (ToBeHiddenAsync, not a count of 0).
public sealed partial class OnboardingPage(App app, AppRoutes routes)
{
    // The headings of the steps (h2), in their order.
    public const string WelcomeHeading = "Meet the 2S Platform";

    public const string ResidencyHeading = "Your platform's data stays in the EU";

    public const string SecurityHeading = "Security first";

    public const string ExhibitionHeading = "Join the exhibition";

    public const string OrganizationHeading = "Create your organization";

    // The first step: the motto under its heading, and the paragraph of the pricing.
    public const string MottoText = "Built to tame the configuration mayhem of complex systems and fast-growing application catalogs, AI-made ones included.";

    public const string PricingText = "The platform is free to use while we shape its pricing. When pricing arrives, it will apply to every customer, existing ones included, and you will hear from us well in advance. It will be based on usage, not on seats, with a generous free tier.";

    // The bold sentence of the callout of the residency step, under its heading.
    public const string HostingLeadText = "All customer data, configuration, secrets and machine access are stored and processed only in the EU (Sweden). The same applies to all AI data processing within the platform.";

    // The purpose of WorkOS in the list of sub-processors, and the second paragraph of its entry.
    public const string WorkOsPurposeText = "Signs in the operators of the 2S Platform and sends their invitation emails. It processes only their name, email, IP address and session data, and the email address of a person they invite.";

    public const string WorkOsNoteText = "Every authorization decision is made and recorded by 2S in the EU.";

    // The lead of the security step, under its heading.
    public const string SecurityLeadText = "Security isn't a feature we added later. It is the ground the 2S Platform stands on.";

    // The paragraphs of the four sections of the security step, in their order.
    public const string SecretsText = "Keep them your way: encrypted with keys in an EU vault, with a key you bring yourself, or encrypted for the one recipient that needs them, so that nobody else can read them, not even us.";

    public const string MachinesText = "No stored passwords, no long-lived keys waiting to leak. A machine proves who it is with its own mTLS certificate or a federated token from the system it runs on, and gets exactly the access its scopes allow.";

    public const string PeopleText = "You sign in with WorkOS AuthKit, and 2S alone decides what you may do. Impersonation is blocked, secrets ask for a recent sign-in, and every change of access lands in an append-only audit log.";

    public const string IdentityProviderText = "Need an EU-only identity provider? Bring your own SSO provider, or host the platform yourself.";

    public const string SecurityTestingText = "Security is built into how we develop the platform, and our testing programme grows with it. We are rolling out automated scanning in our build pipeline and run manual security testing across our REST API, admin interface, CLI and MCP server. Our approach follows recognised industry standards, and every finding is tracked and resolved within defined timelines.";

    // The exhibition step: its story, what it shows, and what checking the box does, under its label.
    public const string ExhibitionText = "Step into Fourth Wall Software, a fictional software house whose customers include Acme Corp and Globex. See how the platform keeps their CRM, HR and CAD solutions configured.";

    public const string ExhibitionPromiseText = "Explore 30 annotations, configurations that resolve live, and the graph that ties them together.";

    public const string ExhibitionChoiceDescriptionText = "Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.";

    // The line under the organization form; its two agreements are links.
    public const string AgreementsText = "By creating an organization or joining the exhibition, you agree to the Customer License Agreement and the Data Processing Agreement.";

    public const string IntroText = "Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.";

    public const string JoiningExhibitionText = "You are joining the exhibition.";

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = WelcomeTitle(), Level = 1 });

    // The list of the steps, above the step that shows. The wizard shows it once it knows whether the deployment has an
    // exhibition, so its buttons never change under the user; the mock has started by then.
    public ILocator StepList => app.Page.GetByRole(AriaRole.Navigation, new() { Name = "Onboarding steps", Exact = true });

    // The buttons of the step list in their order: five, or four without an exhibition.
    public ILocator Steps => StepList.GetByRole(AriaRole.Button);

    public ILocator CurrentStep => StepList.Locator("[aria-current='step']");

    // On every step but the last, where the form has "Create and continue" instead.
    public ILocator NextButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true });

    // On every step but the first; the one of the form is in the page from the start, hidden with its step.
    public ILocator BackButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true });

    // A button styled as a link, below the card.
    public ILocator SignOutButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true });

    public ILocator HostingLead => Text(HostingLeadText);

    // The entries of the list "Sub-processors" of the residency step, in their order; each holds its name, a badge with
    // its region ("EU only" or "USA") and its purpose (WorkOS also a second paragraph), elements of their own.
    public ILocator SubProcessors => app.Page.GetByRole(AriaRole.List, new() { Name = "Sub-processors", Exact = true }).GetByRole(AriaRole.Listitem);

    // The headings of the sections of the step that shows (h3), in their order.
    public ILocator Topics => StepSections.GetByRole(AriaRole.Heading, new() { Level = 3 });

    // The links of the step that shows; each opens in a new tab. On the last step: the two agreements.
    public ILocator StepLinks => StepSections.GetByRole(AriaRole.Link);

    // A Nuxt UI checkbox, a button with the checkbox role.
    public ILocator JoinExhibitionCheckbox => app.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Show me the exhibition", Exact = true });

    public ILocator ExhibitionChoiceDescription => Text(ExhibitionChoiceDescriptionText);

    // The line of the agreements under the organization form: a paragraph with two links, so matched by its text, not
    // by an exact text of one element.
    public ILocator Agreements => StepSections.Locator("p").Filter(new() { HasText = "By creating an organization or joining the exhibition" });

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

    // A button of the step list by its accessible name, "Step <n>: <name>" ("Step 4: Organization" without an
    // exhibition).
    public ILocator Step(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return StepList.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // The heading of a step (h2): WelcomeHeading, ResidencyHeading, SecurityHeading, ExhibitionHeading or
    // OrganizationHeading. Only the step that shows has a visible one.
    public ILocator StepHeading(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true, Level = 2 });
    }

    // A heading inside a step (h3): "Sub-processors", "Secrets", "Machines and pipelines", "People", "Security testing".
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

    // A link of the step that shows by its name, e.g. "Cloud hosting".
    public ILocator Link(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return StepSections.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    }

    // Asserts the links of the step that shows: exactly these, in this order, each with its address and opening in a
    // new tab.
    public async Task ExpectLinksAsync(params (string Name, string Address)[] links)
    {
        ArgumentNullException.ThrowIfNull(links);

        await Expect(StepLinks).ToHaveCountAsync(links.Length);
        for (var index = 0; index < links.Length; index++)
        {
            var (name, address) = links[index];
            var link = StepLinks.Nth(index);
            await Expect(link).ToHaveAccessibleNameAsync(name);
            await Expect(link).ToHaveAttributeAsync("href", address);
            await Expect(link).ToHaveAttributeAsync("target", "_blank");
        }
    }

    // A field error of the form by its exact text.
    public ILocator FieldError(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return app.Page.GetByText(text, new() { Exact = true });
    }

    // Opens the wizard on a step by its address: the first step without one, else "residency", "security",
    // "exhibition" or "organization" (an unknown one, or the exhibition of a deployment without one, shows the first step).
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
