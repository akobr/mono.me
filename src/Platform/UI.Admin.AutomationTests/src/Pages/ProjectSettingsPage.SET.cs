using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the project's Settings: the values of the Project card (dt/dd), the cards kept in
// this browser (the default time zone, the date format and the annotation types; localStorage '2s-admin:project-settings',
// an entry per '<organization>.<project>' with only what was changed), the controls of Machine authentication and the
// Certificate authority, and what the Secrets section shows to a member below Administrator: badges "Administrator",
// disabled fields and no action buttons.
public sealed partial class ProjectSettingsPage
{
    // The localStorage key of every project's browser settings (stores/project-settings.ts).
    public const string BrowserSettingsKey = "2s-admin:project-settings";

    // "Access point", "Your role", "Members" (Administrators only) and, below Administrator, "Description".
    public ILocator ProjectCard => Card("Project", 2);

    // The field error of a description over 500 characters (a rule of the form only; the API has no limit).
    public ILocator DescriptionError => ProjectCard.GetByText("At most 500 characters.", new() { Exact = true });

    public ILocator DefaultTimeZoneHelp => app.Page.GetByText("New annotations in this project start in this time zone. Saved in this browser.", new() { Exact = true });

    public ILocator DatesCard => Card("Dates", 2);

    // The project's date format, for everyone who opens the project in this browser.
    public DateFormatSelect DateFormat => new(app, DatesCard.GetByRole(AriaRole.Combobox, new() { Name = "Date format", Exact = true }));

    public ILocator DateFormatHelp => DatesCard.GetByText(
        "How dates are shown on every page of this project, for everyone who opens it in this browser. Each person can pick their own format in Preferences.",
        new() { Exact = true });

    // Shown in Dates and in Annotation types while Preferences override them.
    public ILocator DatesPreferencesLink => DatesCard.GetByRole(AriaRole.Link, new() { Name = "Open Preferences", Exact = true });

    public ILocator AnnotationTypesCard => Card("Annotation types", 2);

    // The project's names, colors and icons of the types, for everyone who opens the project in this browser.
    public TypeAppearanceEditor ProjectTypes => new(app, AnnotationTypesCard);

    // The footer of Annotation types, for Contributors and up; disabled while the project changes no type.
    public ILocator ResetToBuiltInButton => AnnotationTypesCard.GetByRole(AriaRole.Button, new() { Name = "Reset to built-in", Exact = true });

    public ILocator PersonalTypesNote => AnnotationTypesCard.GetByText("Your own Preferences override some of these for you.", new() { Exact = true });

    public ILocator TypesPreferencesLink => AnnotationTypesCard.GetByRole(AriaRole.Link, new() { Name = "Open Preferences", Exact = true });

    // Below Administrator: the badge "Administrator" and the text instead of the policy.
    public ILocator MachinePolicyAdministratorBadge => MachineAuthenticationCard.GetByText("Administrator", new() { Exact = true });

    public ILocator MachinePolicyAdministratorsOnly => MachineAuthenticationCard.GetByText("The policy is visible and editable for Administrators.", new() { Exact = true });

    // Shown while Certificate or CertificateAndApiKey is enabled; a number from 1 to 3650.
    public ILocator CertificateLifetime => MachineAuthenticationCard.GetByLabel("Default certificate lifetime (days)", new() { Exact = true });

    // The field error of "Enabled credential kinds" with no kind checked.
    public ILocator CredentialKindsError => MachineAuthenticationCard.GetByText("Enable at least one kind.", new() { Exact = true });

    public ILocator CertificateAuthorityCard => Card("Certificate authority", 2);

    public ILocator CertificateAuthorityText => CertificateAuthorityCard.GetByText(
        "The CA that signs machine and shared certificates; servers that verify mTLS clients trust it.",
        new() { Exact = true });

    // Below Administrator, in the header of Allowed levels.
    public ILocator AllowedLevelsAdministratorBadge => AllowedLevelsCard.GetByText("Administrator", new() { Exact = true });

    // The customer key of Bring your own key; disabled below Administrator.
    public ILocator CustomerKeyUri => SecretsSection.GetByRole(AriaRole.Textbox, new() { Name = "Key URI (versionless)", Exact = true });

    // A button of the Secrets section by its exact name ("Rotate now", "Save key", "Verify", "Add recipient", …); none
    // of the administrator actions is there below Administrator. "Rotate now" may be there twice (Standard, HSM).
    public ILocator SecretsButton(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SecretsSection.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // The heading of a card of the page (level 2: "Project", "Annotations", "Dates", "Annotation types", "Machine
    // authentication", "Certificate authority", "Secrets") or of the Secrets section (level 3: "Allowed levels",
    // "Key-encryption keys", "Bring your own key", "Recipients (HYOK)").
    public ILocator CardHeading(string heading, int level = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true, Level = level });
    }

    // The value (dd) of a term of the Project card: "Access point", "Your role", "Members", "Description".
    public ILocator ProjectValue(string term)
    {
        return ProjectTerm(term).Locator("xpath=following-sibling::dd[1]");
    }

    public ILocator ProjectTerm(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return ProjectCard.Locator($"dt:text-is(\"{term}\")");
    }

    // The note of Dates while Preferences show dates in another format: "Your own Preferences show dates as <format>.".
    public ILocator PersonalDateFormatNote(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return DatesCard.GetByText($"Your own Preferences show dates as {format}.", new() { Exact = true });
    }

    // Picks the default kind of new machines among the enabled kinds (their API names) and waits until the select shows
    // it; save it with SaveMachinePolicyAsync.
    public async Task SelectDefaultCredentialKindAsync(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        await DefaultCredentialKind.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = kind, Exact = true }).ClickAsync();
        await Expect(DefaultCredentialKind).ToHaveTextAsync(kind);
    }

    // Types the default certificate lifetime (1 to 3650 days) and leaves the field with Tab, which commits the number;
    // save it with SaveMachinePolicyAsync.
    public async Task SetCertificateLifetimeAsync(int days)
    {
        var text = days.ToString(CultureInfo.InvariantCulture);
        await CertificateLifetime.FillAsync(text);
        await CertificateLifetime.PressAsync("Tab");
        await Expect(CertificateLifetime).ToHaveValueAsync(text);
    }

    // Waits until this browser keeps the value at the path of the project's entry ("defaultTimeZone"; "types", "exe",
    // "icon"), or none for null.
    public async Task ExpectStoredAsync(string? expected, params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await app.ExpectStoredSettingAsync(BrowserSettingsKey, [$"{routes.Organization}.{routes.Project}", .. path], expected);
    }
}
