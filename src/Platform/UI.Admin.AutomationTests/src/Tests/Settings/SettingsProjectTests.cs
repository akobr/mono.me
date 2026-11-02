using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Project and Roles: the Project card of Settings and what each role sees there. An Administrator sees every section
// and changes the description (in a disposable project); a Reader sees everything read-only; a Contributor changes
// the cards kept in this browser but none of the project-wide ones.
[Area(Area.Settings)]
public sealed partial class SettingsProjectTests : PlatformTest
{
    // SET-PRJ-01: Settings page shows every section of the project for an Administrator.
    // Read-only: an Administrator or Owner of the target project sees the Project card with the access point, the role
    // and the number of members, every card and the four cards of the Secrets section, and the administrator controls:
    // the description form, "Save machine policy", the CA download and the secrets policy with Public always allowed.
    [Test]
    [Elementary]
    [TestCase(17)]
    [UserRole(Role.Admin)]
    public async Task Settings_OpenAsAdministrator_ShowsEverySection()
    {
        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();

        await Expect(settings.ProjectValue("Access point")).ToHaveTextAsync($"{Routes.Organization}.{Routes.Project}");
        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync(AdministratorRole());
        await Expect(settings.ProjectValue("Members")).ToHaveTextAsync(MemberCount());

        foreach (var heading in new[] { "Annotations", "Dates", "Annotation types", "Machine authentication", "Certificate authority", "Secrets" })
        {
            await Expect(settings.CardHeading(heading)).ToBeVisibleAsync();
        }

        await Expect(settings.Description).ToBeVisibleAsync();
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();
        await Expect(settings.SaveMachinePolicyButton).ToBeVisibleAsync();
        await Expect(settings.DownloadCaButton).ToBeVisibleAsync();

        foreach (var heading in new[] { "Allowed levels", "Key-encryption keys", "Bring your own key", "Recipients (HYOK)" })
        {
            await Expect(settings.CardHeading(heading, 3)).ToBeVisibleAsync();
        }

        await Expect(settings.AllowLevel("Public")).ToBeCheckedAsync();
        await Expect(settings.AllowLevel("Public")).ToBeDisabledAsync();
        await Expect(settings.SaveSecretsPolicyButton).ToBeDisabledAsync();
    }

    // SET-PRJ-02: Administrator changes the project description and it shows on the organization overview.
    // In a disposable project without a description: 501 characters are refused by the form, exactly 500 are saved and
    // kept after a reload, the organization overview's card carries the whole text in its title, and clearing the
    // description saves an empty one.
    [Test]
    [TestCase(39)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Settings_SaveDescription_ShowsOnOrganizationOverview()
    {
        var project = await CreateDisposableProjectAsync();
        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await Expect(settings.Description).ToHaveValueAsync(string.Empty);
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();

        await settings.Description.FillAsync(PlatformData.Text(501));
        await Expect(settings.DescriptionError).ToBeVisibleAsync();
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();

        // Ends with " (at-<runId>)".
        var description = PlatformData.Text(500);
        await settings.Description.FillAsync(description);
        await Expect(settings.DescriptionError).ToHaveCountAsync(0);
        await settings.SaveDescriptionButton.ClickAsync();
        await Expect(App.Toast("Description saved")).ToBeVisibleAsync();
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();

        await settings.OpenAsync();
        await Expect(settings.Description).ToHaveValueAsync(description);
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();

        var overview = new OrganizationOverview(App, project);
        await overview.OpenAsync();
        await Expect(overview.ProjectCardDescription(project.Project)).ToBeVisibleAsync();
        await Expect(overview.ProjectCardDescription(project.Project)).ToHaveAttributeAsync("title", description);

        // A page load in between, so the toast of the first save is gone.
        await settings.OpenAsync();
        await settings.Description.FillAsync(string.Empty);
        await settings.SaveDescriptionButton.ClickAsync();
        await Expect(App.Toast("Description saved")).ToBeVisibleAsync();

        await settings.OpenAsync();
        await Expect(settings.Description).ToHaveValueAsync(string.Empty);
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();
    }

    // SET-PRJ-03: A Reader sees the project settings read-only.
    // Exactly a Reader of the project: the role and the description as text, without Members and without the form; the
    // browser settings disabled; Machine authentication and the Secrets section (opened through settings#secrets,
    // scrolled into view and focused) marked "Administrator", with disabled fields and no administrator action. The CA
    // download stays for everyone.
    [Test]
    [TestCase(41)]
    [UserRole(Role.Reader)]
    public async Task Settings_OpenAsReader_ShowsEverythingReadOnly()
    {
        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();

        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Reader");
        await Expect(settings.ProjectTerm("Members")).ToHaveCountAsync(0);
        await Expect(settings.ProjectValue("Description")).ToHaveTextAsync(NotBlank());
        await Expect(settings.Description).ToHaveCountAsync(0);
        await Expect(settings.SaveDescriptionButton).ToHaveCountAsync(0);

        var types = settings.ProjectTypes;
        await Expect(settings.DefaultTimeZone.Button).ToBeDisabledAsync();
        await Expect(settings.DateFormat.Combobox).ToBeDisabledAsync();
        await Expect(types.Name(PlatformData.Execution)).ToBeDisabledAsync();
        await Expect(settings.ResetToBuiltInButton).ToHaveCountAsync(0);
        await Expect(types.Reset(PlatformData.Execution)).ToHaveCountAsync(0);

        await Expect(settings.MachinePolicyAdministratorBadge).ToBeVisibleAsync();
        await Expect(settings.MachinePolicyAdministratorsOnly).ToBeVisibleAsync();
        await Expect(settings.SaveMachinePolicyButton).ToHaveCountAsync(0);
        await Expect(settings.DownloadCaButton).ToBeVisibleAsync();

        await settings.OpenAsync("secrets");
        await Expect(settings.SecretsSection).ToBeInViewportAsync();
        await Expect(settings.SecretsSection).ToBeFocusedAsync();
        await Expect(settings.AllowedLevelsAdministratorBadge).ToBeVisibleAsync();
        await Expect(settings.AllowLevel("Standard")).ToBeDisabledAsync();
        await Expect(settings.DefaultSecretLevel).ToBeDisabledAsync();
        await Expect(settings.CustomerKeyUri).ToBeDisabledAsync();

        foreach (var action in new[] { "Save secrets policy", "Rotate now", "Save key", "Verify", "Add recipient" })
        {
            await Expect(settings.SecretsButton(action)).ToHaveCountAsync(0);
        }
    }

    // SET-PRJ-04: A Contributor changes the browser settings but not the project-wide ones.
    // Exactly a Contributor of the project (the role does not follow an Owner role of the organization): the role and
    // the description as text; the browser settings enabled, so an own type name shows in the preview and "Reset to
    // built-in" takes it back; Machine authentication and the Secrets section without any administrator action.
    [Test]
    [TestCase(61)]
    [UserRole(Role.Contributor)]
    public async Task Settings_OpenAsContributor_ChangesOnlyBrowserSettings()
    {
        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();

        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Contributor");
        await Expect(settings.ProjectTerm("Members")).ToHaveCountAsync(0);
        await Expect(settings.ProjectValue("Description")).ToHaveTextAsync(NotBlank());
        await Expect(settings.SaveDescriptionButton).ToHaveCountAsync(0);

        var types = settings.ProjectTypes;
        await Expect(settings.DefaultTimeZone.Button).ToBeEnabledAsync();
        await Expect(settings.DateFormat.Combobox).ToBeEnabledAsync();
        await Expect(types.Name(PlatformData.Execution)).ToBeEnabledAsync();
        await Expect(settings.ResetToBuiltInButton).ToBeVisibleAsync();
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();

        await types.SetNameAsync(PlatformData.Execution, "Job");
        await Expect(types.Reset(PlatformData.Execution)).ToBeVisibleAsync();
        await Expect(settings.ResetToBuiltInButton).ToBeEnabledAsync();

        await settings.ResetToBuiltInButton.ClickAsync();
        await Expect(types.Preview(PlatformData.Execution)).ToHaveTextAsync("Execution");
        await Expect(settings.ResetToBuiltInButton).ToBeDisabledAsync();

        await Expect(settings.MachinePolicyAdministratorBadge).ToBeVisibleAsync();
        await Expect(settings.MachinePolicyAdministratorsOnly).ToBeVisibleAsync();
        await Expect(settings.AllowLevel("Standard")).ToBeDisabledAsync();
        await Expect(settings.SaveMachinePolicyButton).ToHaveCountAsync(0);
        foreach (var action in new[] { "Save secrets policy", "Rotate now", "Add recipient" })
        {
            await Expect(settings.SecretsButton(action)).ToHaveCountAsync(0);
        }
    }

    [GeneratedRegex("^(Owner|Administrator)$")]
    private static partial Regex AdministratorRole();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex MemberCount();

    [GeneratedRegex(@"\S")]
    private static partial Regex NotBlank();
}
