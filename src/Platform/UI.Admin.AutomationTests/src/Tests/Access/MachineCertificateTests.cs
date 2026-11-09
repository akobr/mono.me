using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Machine Certificates: Certificate and CertificateAndApiKey machines and shared certificates, each in a disposable
// project whose machine policy the test changes: enabling the kind, the .pfx download, the shared certificate option,
// the reset of the API key only, and issuing and revoking a shared certificate. The downloaded .pfx files hold secrets
// and are never kept. The cleanup deletes the machines a test created and revokes a shared certificate it left valid
// (certificates cannot be deleted, and disposable projects stay).
[Area(Area.Access)]
public sealed partial class MachineCertificateTests : PlatformTest
{
    // The machines a test created (in the project of its routes), deleted in CleanUpAsync while listed.
    private readonly List<(AppRoutes Routes, string Id)> createdMachines = [];

    // The shared certificates a test issued (by their labels), revoked in CleanUpAsync while valid.
    private readonly List<(AppRoutes Routes, string Label)> issuedCertificates = [];

    // ACC-CRT-01: A Certificate machine shows its certificate once and downloads it.
    // With Certificate enabled in the machine policy, New machine preselects the policy's certificate lifetime; Create
    // shows the .pfx and its password once (no API key), the download is <id>.pfx, and the machine lists its
    // certificate with the days left, Delete but no Reset, and its lifetime and expiry on the detail.
    [Test]
    [TestCase(182)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [CertificateKinds]
    public async Task Certificates_CertificateMachine_DownloadsPfx()
    {
        var project = await CreateDisposableProjectAsync();
        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await settings.EnableCredentialKindAsync("Certificate");
        await Expect(settings.CertificateLifetime).ToBeVisibleAsync();
        await settings.SaveMachinePolicyAsync();
        var lifetime = await settings.CertificateLifetime.InputValueAsync();

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await create.PickCredentialKindAsync("Certificate");
        await Expect(create.CertificateLifetime).ToHaveValueAsync(lifetime);
        await Expect(create.CredentialKindHelp).ToHaveTextAsync("A client certificate per machine (mTLS).");

        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((project, id));
        await Expect(secret.DownloadPfxButton).ToBeVisibleAsync();
        await Expect(secret.CertificatePasswordField).ToBeVisibleAsync();
        await Expect(secret.ApiKeyField).ToHaveCountAsync(0);
        await Expect(secret.DiscardHint).ToHaveTextAsync("Closing this dialog discards them; to replace the certificate, create a new machine.");

        var pfx = await DownloadAsync(() => secret.DownloadPfxButton.ClickAsync(), 30_000);
        await Assert.That(pfx.SuggestedFilename).IsEqualTo($"{id}.pfx");
        await Assert.That(pfx.Content.Length).IsGreaterThan(0);

        await secret.AcknowledgeAsync();
        await Expect(machines.MachineCell(id, "Credentials")).ToHaveTextAsync("Certificate");
        await Expect(machines.CertificateThumbprint(id)).ToHaveTextAsync(ThumbprintStart());
        await Expect(machines.CertificateBadge(id)).ToHaveTextAsync(DaysLeft());
        await Expect(machines.DeleteButton(id)).ToBeVisibleAsync();
        await Expect(machines.ResetButton(id)).ToHaveCountAsync(0);

        var detail = await machines.OpenMachineAsync(id);
        await Expect(detail.Value("Certificate lifetime")).ToHaveTextAsync($"{lifetime.Replace(",", string.Empty, StringComparison.Ordinal)} days");
        await Expect(detail.Value("Certificate expires")).ToBeVisibleAsync();
    }

    // ACC-CRT-02: A CertificateAndApiKey machine uses a shared certificate, and its reset issues only a new API key.
    // With CertificateAndApiKey enabled, "Use a shared certificate of the project" hides the lifetime: the machine gets
    // an API key and no certificate, lists as "shared", and its Reset shows a new API key while the certificate stays
    // valid; the detail names the shared certificate.
    [Test]
    [TestCase(183)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [CertificateKinds]
    public async Task Certificates_SharedCertificateMachine_ResetsApiKeyOnly()
    {
        var project = await CreateDisposableProjectAsync();
        await new ProjectSettingsPage(App, project).SaveMachinePolicyWithKindAsync("CertificateAndApiKey");

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await create.PickCredentialKindAsync("CertificateAndApiKey");
        await Expect(create.CertificateLifetime).ToBeVisibleAsync();
        await Expect(create.UseSharedCertificate).Not.ToBeCheckedAsync();
        await Expect(create.Root.GetByText("A certificate is issued for this machine.", new() { Exact = true })).ToBeVisibleAsync();

        await create.UseSharedCertificate.CheckAsync();
        await Expect(create.CertificateLifetime).ToBeHiddenAsync();
        await Expect(create.Root.GetByText("No certificate is issued for this machine: it signs in with a shared certificate of the project and its API key.", new() { Exact = true })).ToBeVisibleAsync();

        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((project, id));
        await Expect(secret.SharedCertificateAlert).ToBeVisibleAsync();
        await Expect(secret.DownloadPfxButton).ToHaveCountAsync(0);
        var key = await secret.ApiKeyAsync();
        await secret.AcknowledgeAsync();
        await Expect(machines.MachineCell(id, "Credentials")).ToHaveTextAsync("Certificate and API key");
        await Expect(machines.CertificateBadge(id)).ToHaveTextAsync("shared");
        await Expect(machines.ResetButton(id)).ToBeVisibleAsync();

        await machines.ResetButton(id).ClickAsync();
        await machines.ResetDialog(id).ConfirmAsync("Reset credentials");
        var renewed = new OneTimeSecretDialog(App, OneTimeSecretDialog.ResetTitle);
        await Expect(renewed.Root).ToBeVisibleAsync();
        await Assert.That(await renewed.ApiKeyAsync()).IsNotEqualTo(key);
        await Expect(renewed.DiscardHint).ToHaveTextAsync("Closing this dialog discards them; a reset issues a new API key, and the certificate stays valid.");
        await renewed.AcknowledgeAsync();

        var detail = await machines.OpenMachineAsync(id);
        await Expect(detail.Value("Credentials")).ToHaveTextAsync("Certificate and API key");
        await Expect(detail.Value("Certificate")).ToHaveTextAsync("A shared certificate of the project");
    }

    // ACC-CRT-03: Issue a shared certificate, download it and revoke it with its thumbprint prefix.
    // A new project's shared certificates are disabled until the policy enables CertificateAndApiKey; then a certificate
    // issued for 30 days shows its thumbprint and password once and downloads as <label>.pfx, lists as expiring in 29 or
    // 30 days, and Revoke, enabled once the first 8 thumbprint characters are typed, leaves it Revoked.
    [Test]
    [TestCase(184)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [CertificateKinds]
    public async Task Certificates_IssueSharedCertificate_DownloadsAndRevokes()
    {
        var project = await CreateDisposableProjectAsync();
        var label = PlatformData.SharedCertificateLabel();

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        await Expect(machines.NoMachineAccess).ToBeVisibleAsync();
        var certificates = machines.SharedCertificates;
        await Expect(certificates.CertificatesDisabled).ToBeVisibleAsync();
        await Expect(certificates.NoSharedCertificates).ToBeVisibleAsync();
        await Expect(certificates.IssueButton).ToBeDisabledAsync();

        await new ProjectSettingsPage(App, project).SaveMachinePolicyWithKindAsync("CertificateAndApiKey");

        await machines.OpenAsync();
        await certificates.WaitForCertificatesAsync();
        await Expect(certificates.IssueButton).ToBeEnabledAsync();
        await Expect(certificates.CertificatesDisabled).ToHaveCountAsync(0);
        var issue = await certificates.OpenIssueDialogAsync();
        await Expect(issue.Lifetime).ToHaveValueAsync("365");
        await Expect(issue.Label).ToBeEmptyAsync();
        await Expect(issue.IssueButton).ToBeDisabledAsync();

        await issue.Label.FillAsync(label);
        await issue.Lifetime.FillAsync("30");
        await Expect(issue.Lifetime).ToHaveValueAsync("30");
        issuedCertificates.Add((project, label));
        var secret = await issue.IssueAsync();
        var thumbprint = await secret.IdAsync();
        await Expect(secret.DownloadPfxButton).ToBeVisibleAsync();
        await Expect(secret.CertificatePasswordField).ToBeVisibleAsync();
        await Expect(secret.DiscardHint).ToHaveTextAsync("Closing this dialog discards them; to replace the certificate, issue another one.");

        var pfx = await DownloadAsync(() => secret.DownloadPfxButton.ClickAsync(), 30_000);
        await Assert.That(pfx.SuggestedFilename).IsEqualTo($"{label}.pfx");
        await Assert.That(pfx.Content.Length).IsGreaterThan(0);

        await secret.AcknowledgeAsync();
        await Expect(certificates.CertificateRow(label)).ToBeVisibleAsync();
        await Expect(certificates.CertificateCell(label, "Thumbprint")).ToHaveTextAsync($"{thumbprint[..16]}…");
        await Expect(certificates.CertificateCell(label, "Status")).ToHaveTextAsync(ExpiresIn29Or30Days());
        await Expect(certificates.RevokeButton(label)).ToBeVisibleAsync();

        var revoke = certificates.RevokeDialog(label);
        await certificates.RevokeButton(label).ClickAsync();
        await revoke.ExpectOpenAsync();
        await Expect(revoke.Root).ToHaveAccessibleDescriptionAsync("Machines using it can no longer authenticate.");
        await Expect(revoke.Button("Revoke")).ToBeDisabledAsync();
        await Assert.That(await revoke.ConfirmationTextAsync()).IsEqualTo(thumbprint[..8]);

        await revoke.TypeConfirmationAsync(thumbprint[..8]);
        await revoke.ConfirmAsync("Revoke");
        await Expect(App.Toast("Certificate revoked")).ToBeVisibleAsync();
        issuedCertificates.Remove((project, label));
        await Expect(certificates.CertificateCell(label, "Status")).ToHaveTextAsync("Revoked");
        await Expect(certificates.RevokeButton(label)).ToHaveCountAsync(0);
    }

    // Deletes the machines the test created and revokes the shared certificates it left valid, project by project.
    protected override async Task CleanUpAsync()
    {
        foreach (var project in createdMachines.Select(machine => machine.Routes).Concat(issuedCertificates.Select(certificate => certificate.Routes)).Distinct())
        {
            var machines = new MachinesPage(App, project);
            await machines.OpenAsync();
            foreach (var (_, id) in createdMachines.Where(machine => machine.Routes == project))
            {
                await machines.DeleteMachineIfListedAsync(id);
            }

            foreach (var (_, label) in issuedCertificates.Where(certificate => certificate.Routes == project))
            {
                await machines.SharedCertificates.RevokeCertificateIfValidAsync(label);
            }
        }
    }

    // The thumbprint's first 12 characters and an ellipsis (the whole one is in the title).
    [GeneratedRegex(@"^\s*\S{12}…\s*$")]
    private static partial Regex ThumbprintStart();

    // The days left of a machine certificate: "<n> d".
    [GeneratedRegex(@"^\s*\d+ d\s*$")]
    private static partial Regex DaysLeft();

    // A certificate of 30 days, issued just now: the days left are floored.
    [GeneratedRegex(@"^\s*Expires in (29|30) d\s*$")]
    private static partial Regex ExpiresIn29Or30Days();
}
