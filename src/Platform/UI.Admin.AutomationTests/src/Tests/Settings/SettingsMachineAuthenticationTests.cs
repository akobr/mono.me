using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Machine Authentication: the project's machine authentication policy (the enabled credential kinds, the default kind
// of new machines, the default certificate lifetime), changed only in a disposable project, and the CA certificate,
// which every member downloads. What the policy does to New machine belongs to the Access section.
[Area(Area.Settings)]
public sealed partial class SettingsMachineAuthenticationTests : PlatformTest
{
    // Every credential kind of the policy, by its API name, in the API's order.
    private static readonly string[] CredentialKinds = ["ApiKey", "Certificate", "CertificateAndApiKey", "ClientCredentials", "Federated"];

    // SET-MAC-01: Administrator changes the credential kinds, the default kind and the certificate lifetime.
    // A new project allows ApiKey only. A certificate kind shows the lifetime (the deployment's default, 365 on the
    // mock), the default kind follows the enabled kinds, no kind at all is refused, and ApiKey with Certificate, the
    // default Certificate and 30 days are saved and kept after a reload.
    [Test]
    [TestCase(157)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task MachinePolicy_ChangeKindsAndLifetime_IsKept()
    {
        var project = await CreateDisposableProjectAsync();
        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await Expect(settings.SaveMachinePolicyButton).ToBeVisibleAsync();
        await ExpectEnabledKindsAsync(settings, "ApiKey");
        await Expect(settings.DefaultCredentialKind).ToHaveTextAsync("ApiKey");
        await Expect(settings.CertificateLifetime).ToHaveCountAsync(0);

        await settings.CredentialKind("Certificate").CheckAsync();
        await Expect(settings.CertificateLifetime).ToBeVisibleAsync();
        await Expect(settings.CertificateLifetime).ToHaveValueAsync(Target.IsMock ? MockLifetime() : Days());

        await settings.CredentialKind("ApiKey").UncheckAsync();
        await Expect(settings.DefaultCredentialKind).ToHaveTextAsync("Certificate");

        await settings.CredentialKind("Certificate").UncheckAsync();
        await Expect(settings.CredentialKindsError).ToBeVisibleAsync();
        await Expect(settings.SaveMachinePolicyButton).ToBeDisabledAsync();
        await Expect(settings.DefaultCredentialKind).ToBeDisabledAsync();
        await Expect(settings.CertificateLifetime).ToHaveCountAsync(0);

        // Checking ApiKey first makes it the default again, so Certificate is picked afterwards.
        await settings.EnableCredentialKindAsync("ApiKey");
        await settings.EnableCredentialKindAsync("Certificate");
        await Expect(settings.CredentialKindsError).ToHaveCountAsync(0);
        await settings.SelectDefaultCredentialKindAsync("Certificate");
        await settings.SetCertificateLifetimeAsync(30);
        await settings.SaveMachinePolicyAsync();

        await settings.OpenAsync();
        await ExpectEnabledKindsAsync(settings, "ApiKey", "Certificate");
        await Expect(settings.DefaultCredentialKind).ToHaveTextAsync("Certificate");
        await Expect(settings.CertificateLifetime).ToHaveValueAsync("30");
    }

    // SET-MAC-02: Anyone downloads the CA certificate as PEM.
    // Read-only, for any member: the Certificate authority card explains the CA, and its button downloads
    // storyteller-ca.pem, a PEM certificate, without the failure toast.
    [Test]
    [TestCase(158)]
    public async Task CertificateAuthority_Download_SavesPem()
    {
        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        await Expect(settings.CertificateAuthorityText).ToBeVisibleAsync();
        await Expect(settings.DownloadCaButton).ToBeVisibleAsync();

        var file = await DownloadAsync(settings.DownloadCaAsync, timeoutMs: 30_000);
        await Expect(App.Toast("The CA certificate could not be downloaded")).ToHaveCountAsync(0);
        await Assert.That(file.SuggestedFilename).IsEqualTo("storyteller-ca.pem");
        await Assert.That(file.Text).StartsWith("-----BEGIN CERTIFICATE-----");
        await Assert.That(file.Text).Contains("-----END CERTIFICATE-----");
    }

    // Exactly the kinds are checked, the others not; once the policy loaded (the form is shown).
    private static async Task ExpectEnabledKindsAsync(ProjectSettingsPage settings, params string[] enabled)
    {
        foreach (var kind in CredentialKinds)
        {
            await Expect(settings.CredentialKind(kind)).ToBeCheckedAsync(new() { Checked = enabled.Contains(kind, StringComparer.Ordinal) });
        }
    }

    // The mock deployment's default lifetime of new machines' certificates.
    [GeneratedRegex("^365$")]
    private static partial Regex MockLifetime();

    // A deployed target's default lifetime: some number of days.
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Days();
}
