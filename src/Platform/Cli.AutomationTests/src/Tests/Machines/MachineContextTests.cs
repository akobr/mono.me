using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Context: every machine command takes its project from --projectKey; -v and --view change nothing, as machines, trusted
// issuers and the policy belong to the project.
[Area(Area.Machines)]
public sealed class MachineContextTests : MachineAccessTest
{
    // MCH-CTX-01: Every machine command takes its project from --projectKey; -v and --view change nothing.
    // <A> is the target project, set as the default (account set -p <A>); <B> a disposable project with the kinds ApiKey,
    // Certificate and Federated and data in a view <view>. Each machine command runs once with --projectKey <B> --view
    // <view> and once with --projectKey <B> -v <view>: create (a certificate machine -k Certificate -l 7 -o c.pfx and an
    // API key machine), the list, reset, renew (-c c.pfx --password-stdin: the first writes <id1>-renewed.pfx and its
    // password, the second says that the certificate was already renewed), machine-auth (B's policy), issuer set (ci and
    // ci2), issuer, issuer list, rules (of a federated machine created with --projectKey <B> and a rule on ci), delete and
    // issuer delete. Everything happens in B: the machines, the policy and the issuers are B's, and account machine and
    // account machine issuer list of A (the default) show none of them.
    [Test]
    [TestCase(363)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "--projectKey", "-v", "--view")]
    [Covers("account machine", "--projectKey", "-v", "--view")]
    [Covers("account machine reset", "--projectKey", "-v", "--view")]
    [Covers("account machine renew", "--projectKey", "-v", "--view")]
    [Covers("account machine machine-auth", "--projectKey", "-v", "--view")]
    [Covers("account machine issuer set", "--projectKey", "-v", "--view")]
    [Covers("account machine issuer", "--projectKey", "-v", "--view")]
    [Covers("account machine issuer list", "--projectKey", "-v", "--view")]
    [Covers("account machine rules", "--projectKey", "-v", "--view")]
    [Covers("account machine delete", "--projectKey", "-v", "--view")]
    [Covers("account machine issuer delete", "--projectKey", "-v", "--view")]
    public async Task MachineCommands_ContextOptions_TargetProject()
    {
        const string secondIssuerUrl = "https://issuer2.test/";
        var projectA = Target.ProjectKey;
        var projectB = await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Certificate,Federated", projectB);
        var view = CliData.ViewName();
        await CreateAnnotationAsync(projectB, view);
        await CreateSignerAsync();
        (await Sform("account", "set", "-p", projectA)).ShouldSucceed();
        bool[] viewForms = [true, false];

        // The arguments with --projectKey <B> and --view <view> (longView) or -v <view>.
        string[] InB(bool longView, params string[] arguments) => [.. arguments, "--projectKey", projectB, longView ? "--view" : "-v", view];

        var certificateMachine = (await RunCreatingMachineAsync(projectB, InB(true, "account", "machine", "create", "-r", "-k", "Certificate", "-l", "7", "-o", "c.pfx")))
            .ShouldSucceed()
            .ShouldContain("Certificate written to c.pfx");
        var id1 = MachineIdOf(certificateMachine);
        var password = CertificatePasswordOf(certificateMachine);
        var id2 = MachineIdOf((await RunCreatingMachineAsync(projectB, InB(false, "account", "machine", "create", "-r"))).ShouldSucceed());

        foreach (var longView in viewForms)
        {
            var list = (await Sform(InB(longView, "account", "machine"))).ShouldSucceed();
            await Assert.That(RowOf(list, id1)).Contains("Certificate");
            await Assert.That(RowOf(list, id2)).Contains("ApiKey");
        }

        foreach (var longView in viewForms)
        {
            var reset = (await Sform(InB(longView, "account", "machine", "reset", id2))).ShouldSucceed();
            await Assert.That(MachineIdOf(reset)).IsEqualTo(id2);
        }

        (await Sform(new SformRunOptions { Input = password + "\n" }, InB(true, "account", "machine", "renew", id1, "-c", "c.pfx", "--password-stdin")))
            .ShouldSucceed()
            .ShouldContain($"Renewed certificate written to {id1}-renewed.pfx")
            .ShouldContain("Certificate password:");
        await Assert.That(FileExists($"{id1}-renewed.pfx")).IsTrue();
        (await Sform(new SformRunOptions { Input = password + "\n" }, InB(false, "account", "machine", "renew", id1, "-c", "c.pfx", "--password-stdin")))
            .ShouldSucceed()
            .ShouldContain("The certificate for this machine access was already renewed at ");

        foreach (var longView in viewForms)
        {
            var policy = (await Sform(InB(longView, "account", "machine", "machine-auth"))).ShouldSucceed();
            await ShouldPrintPolicyAsync(policy, projectB, ["ApiKey", "Certificate", "Federated"], "ApiKey");
        }

        (await Sform(InB(true, "account", "machine", "issuer", "set", "ci", "--issuer", TestIssuerUrl, "--jwks-file", JwksFile)))
            .ShouldSucceed()
            .ShouldContain($"Trusted issuer ci of {projectB}:");
        (await Sform(InB(false, "account", "machine", "issuer", "set", "ci2", "--issuer", secondIssuerUrl, "--jwks-file", JwksFile)))
            .ShouldSucceed()
            .ShouldContain($"Trusted issuer ci2 of {projectB}:");

        bool BothListed(SformResult result) => CellOf(result, "ci", 1) == TestIssuerUrl && CellOf(result, "ci2", 1) == secondIssuerUrl;
        foreach (var command in new[] { new[] { "account", "machine", "issuer" }, new[] { "account", "machine", "issuer", "list" } })
        {
            foreach (var longView in viewForms)
            {
                var issuers = (await EventuallyAsync(BothListed, IssuerCacheWindow, InB(longView, command))).ShouldSucceed();
                await Assert.That(RowOf(issuers, "ci")[1]).IsEqualTo(TestIssuerUrl);
                await Assert.That(RowOf(issuers, "ci2")[1]).IsEqualTo(secondIssuerUrl);
            }
        }

        var federated = (await RunCreatingMachineAsync(projectB, "account", "machine", "create", "--projectKey", projectB, "-r", "--issuer", "ci", "--audience", TokenEndpointOf(projectB), "--subject", "sub-1"))
            .ShouldSucceed();
        var id3 = MachineIdOf(federated);

        foreach (var longView in viewForms)
        {
            var rules = (await Sform(InB(longView, "account", "machine", "rules", id3))).ShouldSucceed();
            LineWith(rules, "Machine ID:", id3);
            await Assert.That(ValueOf(rules, "Subject")).IsEqualTo("sub-1");
        }

        (await Sform("account", "machine"))
            .ShouldSucceed()
            .ShouldNotContain(id1)
            .ShouldNotContain(id2)
            .ShouldNotContain(id3);
        (await Sform("account", "machine", "issuer", "list"))
            .ShouldSucceed()
            .ShouldNotContain(TestIssuerUrl)
            .ShouldNotContain(secondIssuerUrl);

        (await Sform(InB(true, "account", "machine", "delete", id1))).ShouldSucceed().ShouldContain($"The machine access {id1} has been deleted.");
        (await Sform(InB(false, "account", "machine", "delete", id2))).ShouldSucceed().ShouldContain($"The machine access {id2} has been deleted.");
        (await Sform(InB(true, "account", "machine", "issuer", "delete", "ci2"))).ShouldSucceed().ShouldContain("The trusted issuer ci2 has been deleted.");
        (await Sform(InB(false, "account", "machine", "issuer", "delete", "ci"))).ShouldSucceed().ShouldContain("The trusted issuer ci has been deleted.");

        (await Sform(InB(false, "account", "machine")))
            .ShouldSucceed()
            .ShouldNotContain(id1)
            .ShouldNotContain(id2)
            .ShouldContain(id3);
        bool NoneListed(SformResult result) => TryRowOf(result, "ci") is null && TryRowOf(result, "ci2") is null;
        (await EventuallyAsync(NoneListed, IssuerCacheWindow, InB(true, "account", "machine", "issuer", "list")))
            .ShouldSucceed()
            .ShouldNotContain(TestIssuerUrl)
            .ShouldNotContain(secondIssuerUrl);
    }
}
