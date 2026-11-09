using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Credential Kinds: machines with a certificate (written to a .pfx), a certificate and an API key (also through a shared
// certificate), client credentials, an explicit API key, and the renewal of a certificate.
[Area(Area.Machines)]
public sealed class MachineCredentialTests : MachineAccessTest
{
    // MCH-CRD-01: -k Certificate with -o writes a .pfx, and the machine reads over mTLS.
    // As an Editor, account machine create -r -k Certificate -l 7 -o m.pfx and a second machine with --credential-kind
    // Certificate --lifetime-days 30 --output m2.pfx each print "Certificate written to <file>", the certificate password
    // once ("Make sure to copy the password, …") and the kind Certificate; each .pfx opens with its password and expires
    // in 7 and 30 days; story get <rst> as each machine (the .pfx and its password in the variables) reads.
    [Test]
    [TestCase(304)]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-k", "--credential-kind", "--credential-kind=Certificate", "-o", "--output", "-l", "--lifetime-days")]
    [CertificateKinds]
    public async Task Machine_Certificate_SignsInWithPfx()
    {
        var key = await CreateAnnotationAsync();
        var shortLived = await CreateMachineAsync("-r", "-k", "Certificate", "-l", "7", "-o", "m.pfx");
        var longLived = await CreateMachineAsync("-r", "--credential-kind", "Certificate", "--lifetime-days", "30", "--output", "m2.pfx");

        foreach (var (machine, file, days) in new[] { (shortLived, "m.pfx", 7), (longLived, "m2.pfx", 30) })
        {
            await Assert.That(machine.CredentialKind).IsEqualTo("Certificate");
            machine.Result
                .ShouldContain($"Certificate written to {file}")
                .ShouldContain("Make sure to copy the password, it is not stored anywhere.");
            var password = machine.CertificatePassword ?? throw machine.Result.Fail("printed no certificate password");
            await Assert.That(Regex.Matches(machine.Result.PlainStdOut, Regex.Escape(password)).Count).IsEqualTo(1);
            await Assert.That(FileExists(file)).IsTrue();
            await ShouldExpireInDaysAsync(file, password, days);

            (await Sform(AsMachine(InView(machine.SignInCredentials)), "story", "get", key)).ShouldSucceed();
        }
    }

    // MCH-CRD-02: -k CertificateAndApiKey needs both, and -s uses a shared certificate with the API key.
    // In a disposable project with the kinds ApiKey and CertificateAndApiKey, a shared certificate s.pfx and <rst>:
    // -k CertificateAndApiKey -o m.pfx creates a machine with a certificate of its own and a key, which reads with both;
    // -s and -k CertificateAndApiKey --shared-certificate create CertificateAndApiKey machines without a .pfx ("The machine
    // has no certificate of its own: it presents a shared certificate of the project with its API key (sform account
    // shared-cert).") that read with s.pfx and their keys; -k ApiKey -s ends with -1 before any call: "A shared certificate
    // is used only by certificate-and-api-key machines, not ApiKey."
    [Test]
    [TestCase(305)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "-k", "--credential-kind=CertificateAndApiKey", "-s", "--shared-certificate")]
    [CertificateKinds]
    public async Task Machine_CertificateAndApiKey_SharedCertificate()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,CertificateAndApiKey");
        var shared = await IssueSharedCertificateAsync(CliData.LabelName(), "s.pfx");
        var key = await CreateAnnotationAsync();

        var own = await CreateMachineAsync("-k", "CertificateAndApiKey", "-o", "m.pfx");
        await Assert.That(own.CredentialKind).IsEqualTo("CertificateAndApiKey");
        await Assert.That(own.AccessKey).IsNotNull();
        await Assert.That(own.CertificatePath).IsNotNull();
        own.Result.ShouldContain("Certificate written to m.pfx");
        (await Sform(AsMachine(InView(own.SignInCredentials)), "story", "get", key)).ShouldSucceed();

        foreach (var options in new[] { new[] { "-s" }, new[] { "-k", "CertificateAndApiKey", "--shared-certificate" } })
        {
            var machine = await CreateMachineAsync(options);
            await Assert.That(machine.CredentialKind).IsEqualTo("CertificateAndApiKey");
            await Assert.That(machine.AccessKey).IsNotNull();
            await Assert.That(machine.CertificatePath).IsNull();
            await Assert.That(machine.Result.JsonNode()["UsesSharedCertificate"]?.GetValue<bool>()).IsTrue();
            machine.Result.ShouldContain("The machine has no certificate of its own: it presents a shared certificate of the project with its API key (sform account shared-cert).");

            (await Sform(AsMachine(InView(WithSharedCertificate(machine.SignInCredentials, shared))), "story", "get", key)).ShouldSucceed();
        }

        var apiKey = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "-k", "ApiKey", "-s")))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("A shared certificate is used only by certificate-and-api-key machines, not ApiKey.");
        await Assert.That(TryMachineIdOf(apiKey)).IsNull();
    }

    // MCH-CRD-03: -k ClientCredentials returns a client id and secret that sign in.
    // As an Editor, account machine create -r -k ClientCredentials prints the kind ClientCredentials, "Client ID:", "Client
    // secret:" and "Token URL:" and "Make sure to copy the client secret, it is not stored anywhere."; with the client id
    // and secret in the variables, sform account gets a token ("… with client credentials") and story get <rst> reads.
    [Test]
    [TestCase(306)]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-k", "--credential-kind=ClientCredentials")]
    public async Task Machine_ClientCredentials_SignsIn()
    {
        var key = await CreateAnnotationAsync();
        var machine = await CreateMachineAsync("-r", "-k", "ClientCredentials");

        await Assert.That(machine.CredentialKind).IsEqualTo("ClientCredentials");
        await Assert.That(machine.ClientSecret).IsNotNull();
        machine.Result
            .ShouldContain("Client ID:")
            .ShouldContain("Client secret:")
            .ShouldContain("Token URL:")
            .ShouldContain("Make sure to copy the client secret, it is not stored anywhere.");

        var asMachine = AsMachine(InView(machine.SignInCredentials));
        (await Sform(asMachine, "account"))
            .ShouldSucceed()
            .ShouldContain("with client credentials");
        (await Sform(asMachine, "story", "get", key)).ShouldSucceed();
    }

    // MCH-CRD-04: -k ApiKey explicitly; a kind the project does not enable fails with exit 2, an unknown kind with the
    // parse code.
    // In a disposable project with the kind ApiKey only, account machine create -k ApiKey creates an ApiKey machine;
    // -k Certificate ends with 2 and the server's "The credential kind Certificate is not enabled in the project
    // <org>.<project>."; -k Password ends with -1 before any call: "Unknown credential kind 'Password'. Use ApiKey,
    // Certificate, CertificateAndApiKey, ClientCredentials, or Federated."
    [Test]
    [TestCase(307)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "-k", "--credential-kind=ApiKey")]
    public async Task Machine_DisabledOrUnknownKind_Fails()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey");

        var machine = await CreateMachineAsync("-k", "ApiKey");
        await Assert.That(machine.CredentialKind).IsEqualTo("ApiKey");
        await Assert.That(machine.AccessKey).IsNotNull();

        var disabled = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "-k", "Certificate")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The credential kind Certificate is not enabled in the project {ProjectKey}.");
        await Assert.That(TryMachineIdOf(disabled)).IsNull();

        var unknown = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "-k", "Password")))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("Unknown credential kind 'Password'. Use ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated.");
        await Assert.That(TryMachineIdOf(unknown)).IsNull();
    }

    // MCH-CRD-05: machine renew -c --password-stdin -o renews the certificate of a certificate machine inside the renewal
    // window.
    // As an Editor, a machine of account machine create -r -k Certificate -l 7 -o m.pfx (inside the renewal window, 30 days
    // before expiry); account machine renew <id> -c m.pfx --password-stdin -o renewed.pfx (the password the first line of
    // the input) renews it: 0, "Renewed certificate written to renewed.pfx", "Certificate password: <password>" and "Make
    // sure to copy the password, …", and renewed.pfx opens with that password. account machine renew <id> --certificate
    // m.pfx --password-stdin --output r2.pfx presents the replaced certificate: 0, "The certificate for this machine access
    // was already renewed at …" and "Last renewed at: <time>", no r2.pfx. m.pfx still reads during the overlap.
    [Test]
    [TestCase(308)]
    [UserRole(Role.Editor)]
    [Covers("account machine renew", "arg0", "-c", "--certificate", "--password-stdin", "-o", "--output")]
    [CertificateKinds]
    public async Task Machine_Renew_WritesNewPfx()
    {
        var key = await CreateAnnotationAsync();
        var machine = await CreateMachineAsync("-r", "-k", "Certificate", "-l", "7", "-o", "m.pfx");
        var password = machine.CertificatePassword ?? throw machine.Result.Fail("printed no certificate password");

        var renewed = (await Sform(new SformRunOptions { Input = password + "\n" }, InProject("account", "machine", "renew", machine.Id, "-c", "m.pfx", "--password-stdin", "-o", "renewed.pfx")))
            .ShouldSucceed()
            .ShouldContain("Renewed certificate written to renewed.pfx")
            .ShouldContain("Make sure to copy the password, it is not stored anywhere.");
        var renewedPassword = CertificatePasswordOf(renewed);
        using (var certificate = OpenPfx("renewed.pfx", renewedPassword))
        {
            await Assert.That(certificate.HasPrivateKey).IsTrue();
        }

        (await Sform(new SformRunOptions { Input = password + "\n" }, InProject("account", "machine", "renew", machine.Id, "--certificate", "m.pfx", "--password-stdin", "--output", "r2.pfx")))
            .ShouldSucceed()
            .ShouldContain("The certificate for this machine access was already renewed at ")
            .ShouldContain("Last renewed at: ")
            .ShouldNotContain("Renewed certificate written to");
        await Assert.That(FileExists("r2.pfx")).IsFalse();

        (await Sform(AsMachine(InView(machine.SignInCredentials)), "story", "get", key)).ShouldSucceed();
    }
}
