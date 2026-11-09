using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Authentication Policy: the machine authentication policy of a project (sform account machine machine-auth): the
// enabled credential kinds, the default kind and the default certificate lifetime.
[Area(Area.Machines)]
public sealed class MachineAuthPolicyTests : MachineAccessTest
{
    // MCH-POL-01: machine machine-auth shows the project's policy.
    // As an Editor, account machine machine-auth -p <org>.<project> prints "Machine authentication policy of
    // <org>.<project>:" with the enabled kinds (kinds sform knows), the default kind (one of them) and the certificate
    // lifetime in days.
    [Test]
    [TestCase(329)]
    [UserRole(Role.Editor)]
    [Covers("account machine machine-auth")]
    public async Task MachineAuth_Show_PrintsPolicy()
    {
        var policy = (await Sform(InProject("account", "machine", "machine-auth")))
            .ShouldSucceed()
            .ShouldContain($"Machine authentication policy of {ProjectKey}:");

        var enabled = EnabledKindsOf(policy);
        await Assert.That(enabled.Count).IsGreaterThan(0);
        foreach (var kind in enabled)
        {
            await Assert.That(CredentialKindNames).Contains(kind);
        }

        await Assert.That(enabled).Contains(ValueOf(policy, "Default kind"));
        await Assert.That(ValueOf(policy, "Certificate lifetime")).EndsWith(" days");
    }

    // MCH-POL-02: machine-auth -k -d -l sets the kinds, the default kind and the lifetime, each kind in turn.
    // In a disposable project, as its Owner: -k ApiKey -k Certificate -d Certificate -l 14 enables ApiKey and Certificate
    // with the default Certificate and 14 days, and a machine created with -r (no -k) gets the kind Certificate (a .pfx);
    // --credential-kind ApiKey,ClientCredentials --default-kind ClientCredentials --lifetime-days 30, -k
    // CertificateAndApiKey,Federated -d CertificateAndApiKey, -k Federated,ApiKey -d Federated and -k ApiKey -d ApiKey
    // each print the policy with the enabled kinds, the default kind and the lifetime.
    [Test]
    [TestCase(330)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine machine-auth", "-k", "--credential-kind", "-d", "--default-kind", "-l", "--lifetime-days", "--credential-kind=ApiKey", "--credential-kind=Certificate", "--credential-kind=CertificateAndApiKey", "--credential-kind=ClientCredentials", "--credential-kind=Federated", "--default-kind=ApiKey", "--default-kind=Certificate", "--default-kind=CertificateAndApiKey", "--default-kind=ClientCredentials", "--default-kind=Federated")]
    [CertificateKinds]
    public async Task MachineAuth_Set_ChangesPolicy()
    {
        await CreateDisposableProjectAsync();

        var first = (await Sform(InProject("account", "machine", "machine-auth", "-k", "ApiKey", "-k", "Certificate", "-d", "Certificate", "-l", "14"))).ShouldSucceed();
        await ShouldPrintPolicyAsync(first, ProjectKey, ["ApiKey", "Certificate"], "Certificate", 14);

        var machine = await CreateMachineAsync("-r");
        await Assert.That(machine.CredentialKind).IsEqualTo("Certificate");
        await Assert.That(machine.CertificatePath).IsNotNull();
        await Assert.That(FileExists(machine.CertificatePath!)).IsTrue();

        var second = (await Sform(InProject("account", "machine", "machine-auth", "--credential-kind", "ApiKey,ClientCredentials", "--default-kind", "ClientCredentials", "--lifetime-days", "30"))).ShouldSucceed();
        await ShouldPrintPolicyAsync(second, ProjectKey, ["ApiKey", "ClientCredentials"], "ClientCredentials", 30);

        var third = (await Sform(InProject("account", "machine", "machine-auth", "-k", "CertificateAndApiKey,Federated", "-d", "CertificateAndApiKey"))).ShouldSucceed();
        await ShouldPrintPolicyAsync(third, ProjectKey, ["CertificateAndApiKey", "Federated"], "CertificateAndApiKey");

        var fourth = (await Sform(InProject("account", "machine", "machine-auth", "-k", "Federated,ApiKey", "-d", "Federated"))).ShouldSucceed();
        await ShouldPrintPolicyAsync(fourth, ProjectKey, ["ApiKey", "Federated"], "Federated");

        var fifth = (await Sform(InProject("account", "machine", "machine-auth", "-k", "ApiKey", "-d", "ApiKey"))).ShouldSucceed();
        await ShouldPrintPolicyAsync(fifth, ProjectKey, ["ApiKey"], "ApiKey");
    }

    // MCH-POL-03: A default kind that is not enabled, or -d without -k, fails with the parse code and changes nothing.
    // In a disposable project, as its Owner: machine-auth -k ApiKey -d Certificate ends with -1 and "The --default-kind
    // has to be one of the enabled kinds: ApiKey."; machine-auth -d ApiKey ends with -1 and "At least one
    // --credential-kind is required: ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated."; the
    // policy machine-auth shows afterwards is the one it showed before.
    [Test]
    [TestCase(331)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine machine-auth", "-d")]
    public async Task MachineAuth_DefaultNotEnabled_Fails()
    {
        await CreateDisposableProjectAsync();
        var before = (await Sform(InProject("account", "machine", "machine-auth"))).ShouldSucceed();

        (await Sform(InProject("account", "machine", "machine-auth", "-k", "ApiKey", "-d", "Certificate")))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("The --default-kind has to be one of the enabled kinds: ApiKey.");

        (await Sform(InProject("account", "machine", "machine-auth", "-d", "ApiKey")))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("At least one --credential-kind is required: ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated.");

        var after = (await Sform(InProject("account", "machine", "machine-auth"))).ShouldSucceed();
        await Assert.That(EnabledKindsOf(after)).IsEquivalentTo(EnabledKindsOf(before));
        await Assert.That(ValueOf(after, "Default kind")).IsEqualTo(ValueOf(before, "Default kind"));
        await Assert.That(ValueOf(after, "Certificate lifetime")).IsEqualTo(ValueOf(before, "Certificate lifetime"));
    }
}
