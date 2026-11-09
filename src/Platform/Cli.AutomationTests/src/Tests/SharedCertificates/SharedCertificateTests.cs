using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Tests.Machines;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.SharedCertificates;

// Shared Certificates: the certificates of a project that certificate-and-api-key machines without a certificate of their
// own present with their API keys (sform account shared-cert): issued (a .pfx), listed and revoked. The helpers are the
// machine cases' (MachineAccessTest): every certificate a test issues is revoked when it ends.
[Area(Area.SharedCertificates)]
public sealed class SharedCertificateTests : MachineAccessTest
{
    // CRT-SHC-01: account shared-cert (and certificate) lists the project's shared certificates.
    // As an Administrator, a certificate of account shared-cert issue <label> -p … -o l.pfx is listed by account
    // shared-cert -p … and by its alias account certificate -p …: a row with its label, the start of its thumbprint
    // ("<8 characters>..."), its expiry (the date "Valid: … to <date>" printed) and Active. The certificate is revoked
    // when the test ends.
    [Test]
    [TestCase(321)]
    [UserRole(Role.Admin)]
    [Covers("account shared-cert")]
    [Covers("account certificate")]
    [CertificateKinds]
    public async Task SharedCert_List_ShowsCertificates()
    {
        var certificate = await IssueSharedCertificateAsync(CliData.LabelName(), "l.pfx");

        foreach (var command in new[] { "shared-cert", "certificate" })
        {
            var list = (await EventuallyAsync(result => CellOf(result, certificate.Label, 3) == "Active", SharedCertificateCacheWindow, InProject("account", command)))
                .ShouldSucceed()
                .ShouldContain("Shared certificates");
            var row = RowOf(list, certificate.Label);
            await Assert.That(row[1]).IsEqualTo(certificate.Thumbprint[..8] + "...");
            await Assert.That(row[2]).IsEqualTo(certificate.ValidTo);
            await Assert.That(row[3]).IsEqualTo("Active");
        }
    }

    // CRT-SHC-02: shared-cert issue -l -o writes a .pfx with the label and the lifetime.
    // In a disposable project with the kinds ApiKey and CertificateAndApiKey, account shared-cert issue <label 1> -l 10
    // -o s.pfx and account shared-cert issue <label 2> --lifetime-days 20 --output s2.pfx each print "Shared certificate
    // issued: <label>", its thumbprint and "Certificate written to <file>" with the password; the .pfx files open with
    // their passwords and expire in 10 and 20 days, and account shared-cert lists both labels.
    [Test]
    [TestCase(323)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account shared-cert issue", "arg0", "-l", "--lifetime-days", "-o", "--output")]
    [CertificateKinds]
    public async Task SharedCert_Issue_WritesPfx()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,CertificateAndApiKey");
        var firstLabel = CliData.LabelName();
        var secondLabel = CliData.LabelName();

        var first = await RunIssuingSharedCertificateAsync(ProjectKey, InProject("account", "shared-cert", "issue", firstLabel, "-l", "10", "-o", "s.pfx"));
        var second = await RunIssuingSharedCertificateAsync(ProjectKey, InProject("account", "shared-cert", "issue", secondLabel, "--lifetime-days", "20", "--output", "s2.pfx"));

        foreach (var (certificate, label, file, days) in new[] { (first, firstLabel, "s.pfx", 10), (second, secondLabel, "s2.pfx", 20) })
        {
            await Assert.That(certificate.Label).IsEqualTo(label);
            certificate.Result
                .ShouldContain($"Certificate written to {file}")
                .ShouldContain("Make sure to copy the password, it is not stored anywhere.");
            await ShouldExpireInDaysAsync(file, certificate.Password, days);
        }

        var list = (await EventuallyAsync(result => AllAre(result, "Active", firstLabel, secondLabel), SharedCertificateCacheWindow, InProject("account", "shared-cert"))).ShouldSucceed();
        await Assert.That(RowOf(list, firstLabel)[3]).IsEqualTo("Active");
        await Assert.That(RowOf(list, secondLabel)[3]).IsEqualTo("Active");
    }

    // CRT-SHC-03: shared-cert create is an alias of issue.
    // In a disposable project with the kinds ApiKey and CertificateAndApiKey, account shared-cert create <label> -o s3.pfx
    // issues a certificate as issue does: "Shared certificate issued: <label>", s3.pfx (it opens with the printed
    // password), and the label in account shared-cert.
    [Test]
    [TestCase(332)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account shared-cert create")]
    [CertificateKinds]
    public async Task SharedCert_CreateAlias_Issues()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,CertificateAndApiKey");
        var label = CliData.LabelName();

        var certificate = await RunIssuingSharedCertificateAsync(ProjectKey, InProject("account", "shared-cert", "create", label, "-o", "s3.pfx"));
        await Assert.That(certificate.Label).IsEqualTo(label);
        certificate.Result.ShouldContain("Certificate written to s3.pfx");
        using (var pfx = OpenPfx("s3.pfx", certificate.Password))
        {
            await Assert.That(pfx.HasPrivateKey).IsTrue();
        }

        var list = (await EventuallyAsync(result => AllAre(result, "Active", label), SharedCertificateCacheWindow, InProject("account", "shared-cert"))).ShouldSucceed();
        await Assert.That(RowOf(list, label)[3]).IsEqualTo("Active");
    }

    // CRT-SHC-04: shared-cert revoke (and delete) by thumbprint stops the certificate.
    // In a disposable project with the kinds ApiKey and CertificateAndApiKey, two shared certificates, <rst> and a machine
    // of account machine create -s: account shared-cert revoke <thumbprint 1> and account shared-cert delete <thumbprint 2>
    // each say "Shared certificate <thumbprint> has been revoked."; account shared-cert shows both REVOKED, and story get
    // <rst> as the machine with either certificate and its key ends with 44 (MCH-CRD-02 shows that such a machine reads
    // with a certificate that is not revoked).
    [Test]
    [TestCase(333)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account shared-cert revoke", "arg0")]
    [Covers("account shared-cert delete", "arg0")]
    [CertificateKinds]
    public async Task SharedCert_Revoke_StopsSignIn()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,CertificateAndApiKey");
        var first = await IssueSharedCertificateAsync(CliData.LabelName(), "s1.pfx");
        var second = await IssueSharedCertificateAsync(CliData.LabelName(), "s2.pfx");
        var key = await CreateAnnotationAsync();
        var machine = await CreateMachineAsync("-s");

        // No read before the revocations: the API keeps a shared certificate it looked up in a cache of its instance for 5
        // minutes (CosmosSharedCertificateStore), so a read now could let another instance accept it after the revoke.
        (await Sform(InProject("account", "shared-cert", "revoke", first.Thumbprint)))
            .ShouldSucceed()
            .ShouldContain($"Shared certificate {first.Thumbprint} has been revoked.");
        MarkRevoked(first.Thumbprint);
        (await Sform(InProject("account", "shared-cert", "delete", second.Thumbprint)))
            .ShouldSucceed()
            .ShouldContain($"Shared certificate {second.Thumbprint} has been revoked.");
        MarkRevoked(second.Thumbprint);

        var list = (await EventuallyAsync(result => AllAre(result, "REVOKED", first.Label, second.Label), SharedCertificateCacheWindow, InProject("account", "shared-cert"))).ShouldSucceed();
        foreach (var certificate in new[] { first, second })
        {
            await Assert.That(RowOf(list, certificate.Label)[3]).IsEqualTo("REVOKED");
            (await Sform(AsMachine(InView(WithSharedCertificate(machine.SignInCredentials, certificate))), "story", "get", key))
                .ShouldExitWith(CliExit.Unauthorized);
        }
    }

    // CRT-SHC-05: Issuing a shared certificate below the needed role is refused.
    // As a Contributor of the project (Target:RoleProjects:Contributor), account shared-cert issue <label> -p <contributor
    // project> ends with 44 and "No Administrator access to '<contributor project>'.", and writes no certificate.
    [Test]
    [TestCase(334)]
    [UserRole(Role.Contributor)]
    [Covers("account shared-cert issue")]
    public async Task SharedCert_IssueAsContributor_Refused()
    {
        (await Sform(InProject("account", "shared-cert", "issue", CliData.LabelName())))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.")
            .ShouldNotContain("Certificate written to");
    }

    // CRT-SHC-06: Every shared certificate command takes its project from --projectKey; -v and --view change nothing.
    // <A> is the target project, set as the default (account set -p <A>); <B> a disposable project with the kinds ApiKey and
    // CertificateAndApiKey and data in a view <view>. account shared-cert issue <label 1> -o a.pfx and issue <label 2>
    // -o b.pfx, account shared-cert, and account shared-cert revoke <thumbprint> each run once with --projectKey <B> --view
    // <view> and once with --projectKey <B> -v <view>: the certificates are issued, listed (Active) and revoked (REVOKED)
    // in B, and account shared-cert of A (the default) never lists them.
    [Test]
    [TestCase(335)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account shared-cert issue", "--projectKey", "-v", "--view")]
    [Covers("account shared-cert", "--projectKey", "-v", "--view")]
    [Covers("account shared-cert revoke", "--projectKey", "-v", "--view")]
    [CertificateKinds]
    public async Task SharedCertCommands_ContextOptions_TargetProject()
    {
        var projectA = Target.ProjectKey;
        var projectB = await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,CertificateAndApiKey", projectB);
        var view = CliData.ViewName();
        await CreateAnnotationAsync(projectB, view);
        (await Sform("account", "set", "-p", projectA)).ShouldSucceed();
        bool[] viewForms = [true, false];

        // The arguments with --projectKey <B> and --view <view> (longView) or -v <view>.
        string[] InB(bool longView, params string[] arguments) => [.. arguments, "--projectKey", projectB, longView ? "--view" : "-v", view];

        var first = await RunIssuingSharedCertificateAsync(projectB, InB(true, "account", "shared-cert", "issue", CliData.LabelName(), "-o", "a.pfx"));
        var second = await RunIssuingSharedCertificateAsync(projectB, InB(false, "account", "shared-cert", "issue", CliData.LabelName(), "-o", "b.pfx"));
        await Assert.That(FileExists("a.pfx")).IsTrue();
        await Assert.That(FileExists("b.pfx")).IsTrue();

        foreach (var longView in viewForms)
        {
            var list = (await EventuallyAsync(result => AllAre(result, "Active", first.Label, second.Label), SharedCertificateCacheWindow, InB(longView, "account", "shared-cert"))).ShouldSucceed();
            await Assert.That(RowOf(list, first.Label)[3]).IsEqualTo("Active");
            await Assert.That(RowOf(list, second.Label)[3]).IsEqualTo("Active");
        }

        (await Sform("account", "shared-cert"))
            .ShouldSucceed()
            .ShouldNotContain(first.Label)
            .ShouldNotContain(second.Label);

        (await Sform(InB(true, "account", "shared-cert", "revoke", first.Thumbprint)))
            .ShouldSucceed()
            .ShouldContain($"Shared certificate {first.Thumbprint} has been revoked.");
        MarkRevoked(first.Thumbprint);
        (await Sform(InB(false, "account", "shared-cert", "revoke", second.Thumbprint)))
            .ShouldSucceed()
            .ShouldContain($"Shared certificate {second.Thumbprint} has been revoked.");
        MarkRevoked(second.Thumbprint);

        var revoked = (await EventuallyAsync(result => AllAre(result, "REVOKED", first.Label, second.Label), SharedCertificateCacheWindow, InB(true, "account", "shared-cert"))).ShouldSucceed();
        await Assert.That(RowOf(revoked, first.Label)[3]).IsEqualTo("REVOKED");
        await Assert.That(RowOf(revoked, second.Label)[3]).IsEqualTo("REVOKED");
    }

    // Every label has a row of account shared-cert with the status (Active, REVOKED).
    private static bool AllAre(SformResult result, string status, params string[] labels)
    {
        return labels.All(label => CellOf(result, label, 3) == status);
    }
}
