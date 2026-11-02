using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Key-Values section's members of the settings: a save of the secrets policy that the deployment may refuse, e.g.
// HSM where it has no HSM provider (the API's 501 ProtectionNotConfigured), which the Allowed levels card shows as an
// alert instead of the toast.
public sealed partial class ProjectSettingsPage
{
    // The description of the alert of a refused secrets policy in the Allowed levels card (Nuxt UI's alert; the card has
    // no other description slot). A level still in use shows such an alert too.
    public ILocator AllowedLevelsRefusal => AllowedLevelsCard.Locator("[data-slot='description']");

    // Saves Allowed levels and returns null once the toast "Secrets policy saved" showed, or the text of the card's
    // alert when the API refused the policy.
    public async Task<string?> SaveSecretsPolicyOrReadRefusalAsync()
    {
        var saved = app.Toast("Secrets policy saved");
        await SaveSecretsPolicyButton.ClickAsync();
        await Expect(saved.Or(AllowedLevelsRefusal)).ToBeVisibleAsync();
        return await AllowedLevelsRefusal.IsVisibleAsync() ? (await AllowedLevelsRefusal.InnerTextAsync()).Trim() : null;
    }
}
