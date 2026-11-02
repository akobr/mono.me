using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/settings, a stack of cards (h2): "Project", "Annotations", "Dates", "Annotation types",
// "Machine authentication", "Certificate authority", then the section "Secrets" (settings#secrets, focused once the
// cards above loaded) with its cards (h3) "Allowed levels", "Key-encryption keys", "Bring your own key" and
// "Recipients (HYOK)". The navbar's title is hidden below 2xl, so the "Project" heading is the sign the page is there.
// - Project: the access point and, for an Administrator or Owner, the description with "Save description" (disabled
//   until it changes; the toast "Description saved"); a Reader sees the text only.
// - Annotations: "Default time zone" (Contributor and up), a setting of this browser: no Save, no toast.
// - Machine authentication (Administrator and up): the checkboxes of "Enabled credential kinds" (ApiKey, Certificate,
//   CertificateAndApiKey, ClientCredentials, Federated), "Default for new machines", "Save machine policy" (the toast
//   "Machine policy saved"). A new project enables ApiKey only.
// - Certificate authority: "Download CA certificate (PEM)" (storyteller-ca.pem), for every role.
// - Secrets (Administrator and up change them): a switch "Allow <level>" per level (Public always on), "Default secret
//   level", "Save secrets policy" (the toast "Secrets policy saved"; a refused change is an alert in the card, no
//   toast), and "Add recipient" (Name, Id (optional), Public key: an RSA key of 3072 bits or more as PEM or JWK; the
//   toast "Recipient <id> added"). HYOK needs an enabled recipient, BYOK a verified customer key.
// Machine policy, secrets policy and recipients are project-wide: change them only in a disposable project.
public sealed partial class ProjectSettingsPage(App app, AppRoutes routes)
{
    public ILocator ProjectHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Project", Exact = true, Level = 2 });

    public ILocator Description => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    public ILocator SaveDescriptionButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Save description", Exact = true });

    // The time zone new annotations of the project start in (browser-local; "UTC" by default).
    public TimeZoneSelect DefaultTimeZone => new(app, app.Page.GetByRole(AriaRole.Button, new() { Name = "Default time zone", Exact = true }));

    public ILocator MachineAuthenticationCard => Card("Machine authentication", 2);

    public ILocator DefaultCredentialKind => MachineAuthenticationCard.GetByRole(AriaRole.Combobox, new() { Name = "Default for new machines", Exact = true });

    public ILocator SaveMachinePolicyButton => MachineAuthenticationCard.GetByRole(AriaRole.Button, new() { Name = "Save machine policy", Exact = true });

    public ILocator DownloadCaButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Download CA certificate (PEM)", Exact = true });

    // section#secrets, the target of settings#secrets.
    public ILocator SecretsSection => app.Page.Locator("section#secrets");

    public ILocator AllowedLevelsCard => Card("Allowed levels", 3);

    public ILocator DefaultSecretLevel => AllowedLevelsCard.GetByRole(AriaRole.Combobox, new() { Name = "Default secret level", Exact = true });

    public ILocator SaveSecretsPolicyButton => AllowedLevelsCard.GetByRole(AriaRole.Button, new() { Name = "Save secrets policy", Exact = true });

    public ILocator RecipientsCard => Card("Recipients (HYOK)", 3);

    public ILocator AddRecipientButton => RecipientsCard.GetByRole(AriaRole.Button, new() { Name = "Add recipient", Exact = true });

    // The fields of the open add form of a recipient; required fields' names end in '*'.
    public ILocator RecipientName => RecipientsCard.GetByRole(AriaRole.Textbox, new() { NameRegex = RecipientNameLabel() });

    public ILocator RecipientId => RecipientsCard.GetByRole(AriaRole.Textbox, new() { Name = "Id (optional)", Exact = true });

    public ILocator RecipientPublicKey => RecipientsCard.GetByRole(AriaRole.Textbox, new() { NameRegex = PublicKeyLabel() });

    public ILocator AddButton => RecipientsCard.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });

    // A checkbox of "Enabled credential kinds", by the kind's API name: ApiKey, Certificate, CertificateAndApiKey,
    // ClientCredentials or Federated.
    public ILocator CredentialKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return MachineAuthenticationCard.GetByRole(AriaRole.Checkbox, new() { Name = kind, Exact = true });
    }

    // The switch "Allow <level>" of Allowed levels, by the level's label (Public, Standard, HSM, BYOK, HYOK) or its API
    // name (Hsm, Byok, Hyok).
    public ILocator AllowLevel(string level)
    {
        return AllowedLevelsCard.GetByRole(AriaRole.Switch, new() { Name = $"Allow {ProtectionLevels.Label(level)}", Exact = true });
    }

    // The row of a recipient in the Recipients table, by its exact id.
    public ILocator Recipient(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return RecipientsCard.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell, new() { Name = id, Exact = true }) });
    }

    // Opens the settings, at a section when given (e.g. "secrets" for settings#secrets), and waits for the "Project"
    // card and the section.
    public async Task OpenAsync(string? section = null)
    {
        await app.GotoAsync(routes.Settings(section));
        await app.WaitForReadyAsync();
        await Expect(ProjectHeading).ToBeVisibleAsync();
        if (section is not null)
        {
            await Expect(app.Page.Locator($"#{section}")).ToBeVisibleAsync();
        }
    }

    public async Task SaveDescriptionAsync(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        await Description.FillAsync(description);
        await SaveDescriptionButton.ClickAsync();
        await Expect(app.Toast("Description saved")).ToBeVisibleAsync();
    }

    // Checks the kind (its API name) in "Enabled credential kinds", once the policy loaded; save it with
    // SaveMachinePolicyAsync.
    public async Task EnableCredentialKindAsync(string kind)
    {
        await CredentialKind(kind).SetCheckedAsync(true);
    }

    // Saves the machine policy and waits for the toast "Machine policy saved".
    public async Task SaveMachinePolicyAsync()
    {
        await SaveMachinePolicyButton.ClickAsync();
        await Expect(app.Toast("Machine policy saved")).ToBeVisibleAsync();
    }

    // Clicks "Download CA certificate (PEM)": the action of PlatformTest.DownloadAsync, which returns storyteller-ca.pem.
    public async Task DownloadCaAsync()
    {
        await DownloadCaButton.ClickAsync();
    }

    // Turns the switch of the level on (or off) in Allowed levels, once the policy loaded; save it with
    // SaveSecretsPolicyAsync.
    public async Task AllowLevelAsync(string level, bool allowed = true)
    {
        await AllowLevel(level).SetCheckedAsync(allowed);
    }

    // Saves Allowed levels, the default level and the mTLS rule, and waits for the toast "Secrets policy saved". Turning
    // the mTLS rule off asks "Deliver secrets to machines without mTLS?" first; a refused change shows an alert in the
    // card instead of the toast.
    public async Task SaveSecretsPolicyAsync()
    {
        await SaveSecretsPolicyButton.ClickAsync();
        await Expect(app.Toast("Secrets policy saved")).ToBeVisibleAsync();
    }

    // Adds a HYOK recipient: "Add recipient", Name, Id (optional; null lets the API generate one) and Public key (PEM or
    // JWK), then "Add". Returns once the toast "Recipient <id> added" showed and the form closed.
    public async Task AddRecipientAsync(string name, string? id, string publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        await AddRecipientButton.ClickAsync();
        await RecipientName.FillAsync(name);
        if (id is not null)
        {
            await RecipientId.FillAsync(id);
        }

        await RecipientPublicKey.FillAsync(publicKey);
        await AddButton.ClickAsync();
        await Expect(id is null ? app.Toast(RecipientAdded()) : app.Toast($"Recipient {id} added")).ToBeVisibleAsync();
        await Expect(AddButton).ToBeHiddenAsync();
    }

    // A card of the page by its heading (level 2, or 3 inside Secrets).
    private ILocator Card(string heading, int level)
    {
        return NuxtLocators.Card(app.Page.Locator(":root"), heading, level);
    }

    [GeneratedRegex(@"^Name\*?$")]
    private static partial Regex RecipientNameLabel();

    [GeneratedRegex(@"^Public key\*?$")]
    private static partial Regex PublicKeyLabel();

    [GeneratedRegex("^Recipient .+ added$")]
    private static partial Regex RecipientAdded();
}
