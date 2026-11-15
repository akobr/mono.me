using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the project's Settings: the values of the Project card (dt/dd), the project
// settings that the API keeps for everyone in the project (the default time zone, the date format and the annotation
// types: one draft that an Administrator or Owner saves with "Save project settings"; read-only below Administrator,
// with the badge "Administrator" in each of the three cards), the banner of the settings this browser kept before the
// API stored them, the controls of Machine authentication and the Certificate authority, and what the Secrets section
// shows to a member below Administrator: badges "Administrator", disabled fields and no action buttons. Saved project
// settings stay for every later test and person: save them only in a disposable project.
public sealed partial class ProjectSettingsPage
{
    // The text under the header of each of the three cards of the project settings, below Administrator.
    public const string AdministratorsOnlyText = "Only Administrators change the project settings.";

    // The title of the banner of the settings this browser kept before the API stored them.
    public const string LegacySettingsTitle = "Project settings of this browser";

    // "Access point", "Your role", "Members" (Administrators only) and, below Administrator, "Description".
    public ILocator ProjectCard => Card("Project", 2);

    // The field error of a description over 500 characters (a rule of the form only; the API has no limit).
    public ILocator DescriptionError => ProjectCard.GetByText("At most 500 characters.", new() { Exact = true });

    public ILocator AnnotationsCard => Card("Annotations", 2);

    public ILocator DefaultTimeZoneHelp => AnnotationsCard.GetByText("New annotations start in this time zone, for everyone in this project.", new() { Exact = true });

    // The hint of "Default time zone" (Administrators): sets the draft's zone to UTC, which is saved as unset.
    public ILocator UseDefaultTimeZoneButton => AnnotationsCard.GetByRole(AriaRole.Button, new() { Name = "Use the default: UTC", Exact = true });

    public ILocator DatesCard => Card("Dates", 2);

    // The project's date format, for everyone in the project (unless their Preferences pick their own).
    public DateFormatSelect DateFormat => new(app, DatesCard.GetByRole(AriaRole.Combobox, new() { Name = "Date format", Exact = true }));

    public ILocator DateFormatHelp => DatesCard.GetByText(
        "How dates are shown on every page, for everyone in this project. Each person can pick their own format in Preferences.",
        new() { Exact = true });

    // The hint of "Date format" (Administrators): sets the draft's format to yyyy-mm-dd, which is saved as unset.
    public ILocator UseDefaultDateFormatButton => DatesCard.GetByRole(AriaRole.Button, new() { Name = "Use the default: yyyy-mm-dd", Exact = true });

    // Shown in Dates and in Annotation types while Preferences override them.
    public ILocator DatesPreferencesLink => DatesCard.GetByRole(AriaRole.Link, new() { Name = "Open Preferences", Exact = true });

    public ILocator AnnotationTypesCard => Card("Annotation types", 2);

    public ILocator AnnotationTypesHelp => AnnotationTypesCard.GetByText(
        "Names, colors and icons of the annotation types on every page, for everyone in this project. Each person can override them in Preferences on the account page.",
        new() { Exact = true });

    // The project's names, colors and icons of the types, for everyone in the project: the draft of the page. Its
    // previews show the draft at once, the rest of the app (and the CSS variables --st-<code>) once it is saved.
    public TypeAppearanceEditor ProjectTypes => new(app, AnnotationTypesCard);

    // The footer of Annotation types, for Administrators and Owners: empties the draft's types (saved with "Save project
    // settings"); disabled while the draft changes no type.
    public ILocator ResetToBuiltInButton => AnnotationTypesCard.GetByRole(AriaRole.Button, new() { Name = "Reset to built-in", Exact = true });

    // Below Annotation types, for Administrators and Owners: saves the draft of the three cards with one write (the toast
    // "Project settings saved"); disabled while the draft equals the saved settings, and "Unsaved changes" beside it
    // while it differs.
    public ILocator SaveSettingsButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Save project settings", Exact = true });

    public ILocator UnsavedSettingsNote => app.Page.GetByText("Unsaved changes", new() { Exact = true });

    // The banner of the settings this browser kept for the project before the API stored them (localStorage
    // '2s-admin:project-settings'), shown while the project has none on the server: "Save for the project" and
    // "Discard" for an Administrator, a request to ask one for anyone else.
    public ILocator LegacySettingsBanner => NuxtLocators.Alert(app.Page.Locator(":root"), LegacySettingsTitle);

    public ILocator SaveLegacySettingsButton => LegacySettingsBanner.GetByRole(AriaRole.Button, new() { Name = "Save for the project", Exact = true });

    public ILocator DiscardLegacySettingsButton => LegacySettingsBanner.GetByRole(AriaRole.Button, new() { Name = "Discard", Exact = true });

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

    // Below Administrator, in the header of a card of the project settings: "Annotations", "Dates" or "Annotation types".
    public ILocator SettingsAdministratorBadge(string card)
    {
        return SettingsCard(card).GetByText("Administrator", new() { Exact = true });
    }

    // Below Administrator, the text under the header of a card of the project settings.
    public ILocator SettingsAdministratorsOnly(string card)
    {
        return SettingsCard(card).GetByText(AdministratorsOnlyText, new() { Exact = true });
    }

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
        await app.PickOptionAsync(DefaultCredentialKind, kind);
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

    // Waits until the project settings loaded, for an Administrator or Owner: until then the fields are disabled and show
    // the defaults, so a check of the saved values after a page load comes after this.
    public async Task WaitForEditableSettingsAsync()
    {
        await Expect(DefaultTimeZone.Button).ToBeEnabledAsync();
        await Expect(DateFormat.Combobox).ToBeEnabledAsync();
    }

    // Saves the draft of the project settings with "Save project settings" and waits for the toast "Project settings
    // saved" and the disabled button.
    public async Task SaveSettingsAsync()
    {
        await SaveSettingsButton.ClickAsync();
        await Expect(app.Toast("Project settings saved")).ToBeVisibleAsync();
        await Expect(SaveSettingsButton).ToBeDisabledAsync();
    }

    // Loads the settings again, at the section when given, and waits like OpenAsync. Where the page shows them already,
    // this reloads the page: a navigation to the same address with another #section only scrolls, and keeps what the
    // page read before.
    public async Task ReloadAsync(string? section = null)
    {
        var url = new Uri(app.Page.Url);
        if (!string.Equals(url.AbsolutePath.Trim('/'), routes.Settings().Trim('/'), StringComparison.Ordinal))
        {
            await OpenAsync(section);
            return;
        }

        await app.Page.ReloadAsync();
        if (section is not null && url.Fragment != $"#{section}")
        {
            await app.GotoAsync(routes.Settings(section));
        }

        await app.WaitForReadyAsync();
        await Expect(ProjectHeading).ToBeVisibleAsync();
        if (section is not null)
        {
            await Expect(app.Page.Locator($"#{section}")).ToBeVisibleAsync();
        }
    }

    // A card of the project settings by its heading: "Annotations", "Dates" or "Annotation types".
    private ILocator SettingsCard(string card)
    {
        return card switch
        {
            "Annotations" => AnnotationsCard,
            "Dates" => DatesCard,
            "Annotation types" => AnnotationTypesCard,
            _ => throw new ArgumentException($"'{card}' is not a card of the project settings; use Annotations, Dates or Annotation types.", nameof(card)),
        };
    }
}
