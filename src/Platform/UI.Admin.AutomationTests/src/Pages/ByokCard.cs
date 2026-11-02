using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The card "Bring your own key" of the Secrets section (settings#secrets; components/secrets/ByokCard.vue): the badge
// of the customer key's health in its header (once a key is set), "1. Consent and role" ("Show the setup", which
// depends on the deployment's BYOK application), "2. The key" ("Key URI (versionless)", "Tenant id" and, for
// Administrators, "Save key", disabled until both are filled and differ from the saved key; the toast "Customer key
// saved; verify it next") and "3. Verify" ("Save the key first." without a key; Health, Verified, Key version, Last
// check and "Verify" with one). A refused save is an error alert in the card. Saving the key refetches the policy,
// which resets the switches of Allowed levels that were not saved.
public sealed partial class ByokCard(App app, AppRoutes routes)
{
    private readonly ProjectSettingsPage settings = new(app, routes);

    public ILocator Root => app.Page
        .GetByRole(AriaRole.Heading, new() { Name = "Bring your own key", Exact = true, Level = 3 })
        .Locator("xpath=ancestor::*[@data-slot='root'][1]");

    // The health of the saved key in the card's header ("Unknown", "Healthy", "Unavailable"); none without a key.
    public ILocator HealthBadge => Root.Locator("[data-slot='header'] [data-slot='label']");

    public ILocator KeyUri => settings.CustomerKeyUri;

    public ILocator TenantId => Root.GetByRole(AriaRole.Textbox, new() { Name = "Tenant id", Exact = true });

    public ILocator SaveKeyButton => Root.GetByRole(AriaRole.Button, new() { Name = "Save key", Exact = true });

    // Shown instead of the verification while no key is saved.
    public ILocator SaveTheKeyFirst => Root.GetByText("Save the key first.", new() { Exact = true });

    // Never clicked by the tests: a deployed target verifies a real key in the customer's vault.
    public ILocator VerifyButton => Root.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true });

    // The value of a term of "3. Verify": "Health", "Verified", "Key version", "Last check", "Last error".
    public ILocator Verification(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return Root.Locator($"dt:text-is(\"{term}\") + dd");
    }

    // Fills the key URI and the tenant id, saves the key and waits for the toast "Customer key saved; verify it next".
    public async Task SaveKeyAsync(string keyUri, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        await KeyUri.FillAsync(keyUri);
        await TenantId.FillAsync(tenantId);
        await SaveKeyButton.ClickAsync();
        await Expect(app.Toast("Customer key saved; verify it next")).ToBeVisibleAsync();
    }
}
