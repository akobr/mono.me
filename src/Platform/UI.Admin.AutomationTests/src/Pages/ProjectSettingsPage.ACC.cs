using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Access section's part of the project's settings: the setup step of the machine cases in a disposable project,
// one more credential kind in the machine policy.
public sealed partial class ProjectSettingsPage
{
    // Opens the settings, checks the kind (its API name) in "Enabled credential kinds" and saves the machine policy;
    // returns once the toast "Machine policy saved" showed. The policy is project-wide: only in a disposable project.
    public async Task SaveMachinePolicyWithKindAsync(string kind)
    {
        await OpenAsync();
        await EnableCredentialKindAsync(kind);
        await Expect(CredentialKind(kind)).ToBeCheckedAsync();
        await SaveMachinePolicyAsync();
    }
}
