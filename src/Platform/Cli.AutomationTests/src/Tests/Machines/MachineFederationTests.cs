using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Federation: the trusted issuers of a project (sform account machine issuer) and federated machines, whose workloads
// exchange a token of their platform for a Storyteller token. The platform is the test's own (PlatformTokenSigner): an
// RSA key pair whose key set the issuer stores (--jwks-file), and the tokens it signs for the project's token endpoint,
// given to sform as STORYTELLER_TOKEN_FILE with the machine id as STORYTELLER_CLIENT_ID.
[Area(Area.Machines)]
public sealed class MachineFederationTests : MachineAccessTest
{
    // MCH-FED-01: machine issuer set with --jwks-file stores an issuer, and issuer list and issuer show it.
    // In a disposable project, account machine issuer set ci --issuer https://issuer.test/ --jwks-file jwks.json
    // --max-lifetime 600 --require-claim repo=a --require-claim repo=b --description "CLI tests" prints the issuer (its
    // URL, the stored key set, 600 s, both alternatives of repo and the description); account machine issuers list and
    // account machine issuer list it with those settings.
    [Test]
    [TestCase(337)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine issuer set", "arg0", "--issuer", "--jwks-file", "--max-lifetime", "--require-claim", "--description")]
    [Covers("account machine issuers list")]
    [Covers("account machine issuer")]
    public async Task Issuer_SetWithJwksFile_Listed()
    {
        await CreateDisposableProjectAsync();
        await CreateSignerAsync();

        var set = (await Sform(InProject("account", "machine", "issuer", "set", "ci", "--issuer", TestIssuerUrl, "--jwks-file", JwksFile, "--max-lifetime", "600", "--require-claim", "repo=a", "--require-claim", "repo=b", "--description", "CLI tests")))
            .ShouldSucceed()
            .ShouldContain($"Trusted issuer ci of {ProjectKey}:");
        await Assert.That(ValueOf(set, "Issuer")).IsEqualTo(TestIssuerUrl);
        await Assert.That(ValueOf(set, "Keys")).IsEqualTo("Jwks (stored key set)");
        await Assert.That(ValueOf(set, "Max lifetime")).IsEqualTo("600 s");
        await Assert.That(ValueOf(set, "Description")).IsEqualTo("CLI tests");
        LineWith(set, "Required claim:", "repo=a");
        LineWith(set, "Required claim:", "repo=b");

        foreach (var list in new[] { InProject("account", "machine", "issuers", "list"), InProject("account", "machine", "issuer") })
        {
            var listed = (await EventuallyAsync(result => CellOf(result, "ci", 3) == "600 s", IssuerCacheWindow, list)).ShouldSucceed().ShouldContain("Trusted issuers");
            var row = RowOf(listed, "ci");
            await Assert.That(row.Count).IsEqualTo(5);
            await Assert.That(row[1]).IsEqualTo(TestIssuerUrl);
            await Assert.That(row[2]).IsEqualTo("Jwks (stored key set)");
            await Assert.That(row[3]).IsEqualTo("600 s");
            await Assert.That(row[4]).Contains("repo=a");
            await Assert.That(row[4]).Contains("repo=b");
        }
    }

    // MCH-FED-02: issuer create --jwks-uri replaces settings not given, and the issuer URL of an existing issuer cannot
    // change.
    // In a disposable project with the issuer ci (https://issuer.test/, jwks.json, --description "CLI tests",
    // --require-claim repo=a), account machine issuer create ci --issuer https://issuer.test/ --jwks-uri <url> replaces it:
    // its keys come from the JWKS URL, and the description and the claims are gone. issuer create ci --issuer
    // https://other.test/ --jwks-uri <url> ends with 2 and the server's "The issuer of a trusted issuer cannot change; create
    // a new one."; the issuer keeps https://issuer.test/.
    [Test]
    [TestCase(338)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine issuer create", "--jwks-uri")]
    public async Task Issuer_Replace_ClearsSettings()
    {
        const string jwksUri = "https://issuer.test/.well-known/jwks.json";
        await CreateDisposableProjectAsync();
        await CreateSignerAsync();
        await SetIssuerAsync("ci", TestIssuerUrl, null, "--description", "CLI tests", "--require-claim", "repo=a");

        var replaced = (await Sform(InProject("account", "machine", "issuer", "create", "ci", "--issuer", TestIssuerUrl, "--jwks-uri", jwksUri)))
            .ShouldSucceed()
            .ShouldContain($"Trusted issuer ci of {ProjectKey}:")
            .ShouldNotContain("Description:")
            .ShouldNotContain("Required claim:");
        await Assert.That(ValueOf(replaced, "Keys")).IsEqualTo($"JwksUri {jwksUri}");

        bool Replaced(SformResult result) => CellOf(result, "ci", 2) == $"JwksUri {jwksUri}";
        var replacedRow = RowOf((await EventuallyAsync(Replaced, IssuerCacheWindow, InProject("account", "machine", "issuer", "list"))).ShouldSucceed(), "ci");
        await Assert.That(replacedRow[1]).IsEqualTo(TestIssuerUrl);
        await Assert.That(replacedRow[2]).IsEqualTo($"JwksUri {jwksUri}");
        await Assert.That(replacedRow.Any(cell => cell.Contains("repo=a", StringComparison.Ordinal))).IsFalse();

        (await Sform(InProject("account", "machine", "issuer", "create", "ci", "--issuer", "https://other.test/", "--jwks-uri", jwksUri)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The issuer of a trusted issuer cannot change; create a new one.");

        var keptRow = RowOf((await EventuallyAsync(Replaced, IssuerCacheWindow, InProject("account", "machine", "issuer", "list"))).ShouldSucceed(), "ci");
        await Assert.That(keptRow[1]).IsEqualTo(TestIssuerUrl);
    }

    // MCH-FED-03: issuer list works for a Contributor; issuer set by a Contributor is refused.
    // As a Contributor of the project (Target:RoleProjects:Contributor), account machine issuer list prints the trusted
    // issuers; account machine issuer set x --issuer https://issuer.test/ ends with 44 and "No Administrator access to
    // '<contributor project>'.".
    [Test]
    [TestCase(339)]
    [UserRole(Role.Contributor)]
    [Covers("account machine issuer list")]
    [Covers("account machine issuer set")]
    public async Task Issuer_ContributorRights()
    {
        (await Sform(InProject("account", "machine", "issuer", "list")))
            .ShouldSucceed()
            .ShouldContain("Trusted issuers");

        (await Sform(InProject("account", "machine", "issuer", "set", "x", "--issuer", TestIssuerUrl)))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.");
    }

    // MCH-FED-04: issuer delete (and remove) deletes an issuer, even with rules referencing it.
    // In a disposable project with the kinds ApiKey and Federated, the issuers a (https://a.test/) and b (https://b.test/)
    // of jwks.json and a federated machine with a rule on a: account machine issuer delete a and account machine issuer
    // remove b each say "The trusted issuer <name> has been deleted.", and account machine issuer list shows neither.
    [Test]
    [TestCase(340)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine issuer delete", "arg0")]
    [Covers("account machine issuer remove", "arg0")]
    public async Task Issuer_Delete_Removes()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Federated");
        await CreateSignerAsync();
        await SetIssuerAsync("a", "https://a.test/");
        await SetIssuerAsync("b", "https://b.test/");
        var machine = await CreateMachineAsync("-r", "--issuer", "a", "--audience", TokenEndpointOf(ProjectKey), "--subject", "s");
        await Assert.That(machine.CredentialKind).IsEqualTo("Federated");

        (await Sform(InProject("account", "machine", "issuer", "delete", "a")))
            .ShouldSucceed()
            .ShouldContain("The trusted issuer a has been deleted.");
        (await Sform(InProject("account", "machine", "issuer", "remove", "b")))
            .ShouldSucceed()
            .ShouldContain("The trusted issuer b has been deleted.");

        static bool Gone(SformResult result) => !result.PlainStdOut.Contains("https://a.test/", StringComparison.Ordinal) && !result.PlainStdOut.Contains("https://b.test/", StringComparison.Ordinal);
        (await EventuallyAsync(Gone, IssuerCacheWindow, InProject("account", "machine", "issuer", "list")))
            .ShouldSucceed()
            .ShouldNotContain("https://a.test/")
            .ShouldNotContain("https://b.test/");
    }

    // MCH-FED-05: A federated machine created with --issuer --audience --subject --claim exchanges a token the test signs.
    // In a disposable project with the kinds ApiKey and Federated and the issuer ci of the test's key set, account machine
    // create -r --issuer ci --audience <token endpoint> --subject repo:x:* --claim env=prod creates a Federated machine (the
    // rule implies the kind) without a secret; with a token the test signed (sub repo:x:main, env prod) in
    // STORYTELLER_TOKEN_FILE, sform account exchanges it ("… with workload identity federation") and story get <rst> reads.
    [Test]
    [TestCase(357)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "--issuer", "--audience", "--subject", "--claim")]
    public async Task Machine_Federated_ExchangesToken()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Federated");
        var signer = await CreateSignerAsync();
        await SetIssuerAsync("ci");
        var key = await CreateAnnotationAsync();

        var machine = await CreateMachineAsync("-r", "--issuer", "ci", "--audience", TokenEndpointOf(ProjectKey), "--subject", "repo:x:*", "--claim", "env=prod");
        await Assert.That(machine.CredentialKind).IsEqualTo("Federated");
        await Assert.That(machine.AccessKey).IsNull();
        machine.Result
            .ShouldContain("The machine has no secret: its workload signs in with a token of its platform that matches a rule.")
            .ShouldContain("Subject:  repo:x:*")
            .ShouldContain("Claim:    env=prod");

        var asMachine = AsMachine(await FederatedAsync(machine.Id, signer, "repo:x:main", Claim("env", "prod")));
        (await Sform(asMachine, "account"))
            .ShouldSucceed()
            .ShouldContain($"Signed in as machine {machine.Id} with workload identity federation");
        (await Sform(asMachine, "story", "get", key)).ShouldSucceed();
    }

    // MCH-FED-06: A federated machine from -k Federated --rules-file matches one of several rules.
    // In a disposable project with the kinds ApiKey and Federated and the issuer ci, account machine create -r -k Federated
    // --rules-file rules.json (two rules on ci: sub-a, and sub-b with env prod) creates a Federated machine with both rules;
    // sform account accepts a token of each rule.
    [Test]
    [TestCase(358)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "--rules-file", "--credential-kind=Federated")]
    public async Task Machine_FederatedRulesFile_Matches()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Federated");
        var signer = await CreateSignerAsync();
        await SetIssuerAsync("ci");
        var audience = TokenEndpointOf(ProjectKey);
        await WriteFileAsync("rules.json", RulesFile(Rule("ci", audience, "sub-a"), Rule("ci", audience, "sub-b", ("env", "prod"))));

        var machine = await CreateMachineAsync("-r", "-k", "Federated", "--rules-file", "rules.json");
        await Assert.That(machine.CredentialKind).IsEqualTo("Federated");
        machine.Result
            .ShouldContain("Rule 1:")
            .ShouldContain("Rule 2:")
            .ShouldContain("Subject:  sub-a")
            .ShouldContain("Subject:  sub-b");

        (await Sform(AsMachine(await FederatedAsync(machine.Id, signer, "sub-a")), "account")).ShouldSucceed();
        (await Sform(AsMachine(await FederatedAsync(machine.Id, signer, "sub-b", Claim("env", "prod"))), "account")).ShouldSucceed();
    }

    // MCH-FED-07: machine rules shows the rules, and replaces them with options or a file.
    // In a disposable project with the kinds ApiKey and Federated, the issuer ci and a federated machine <id> with a rule
    // on ci (as in MCH-FED-05, whose token signs in): account machine rules <id> shows the rule; account machine rules <id>
    // --issuer ci --audience … --subject sub-2 --claim k=v replaces it ("The federation rules of <id> have been replaced;
    // …") and shows the new rule, after which the token of the old rule is refused and one of the new rule accepted;
    // account machine rules <id> --rules-file rules.json replaces it with the rule of the file, after which the token of
    // the second rule is refused and one of the file's rule accepted.
    [Test]
    [TestCase(359)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine rules", "arg0", "--issuer", "--audience", "--subject", "--claim", "--rules-file")]
    public async Task MachineRules_ShowAndReplace()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Federated");
        var signer = await CreateSignerAsync();
        await SetIssuerAsync("ci");
        var audience = TokenEndpointOf(ProjectKey);
        var machine = await CreateMachineAsync("-r", "--issuer", "ci", "--audience", audience, "--subject", "repo:x:*", "--claim", "env=prod");
        var first = AsMachine(await FederatedAsync(machine.Id, signer, "repo:x:main", Claim("env", "prod")));
        (await Sform(first, "account")).ShouldSucceed();

        var shown = (await Sform(InProject("account", "machine", "rules", machine.Id))).ShouldSucceed();
        LineWith(shown, "Machine ID:", machine.Id);
        await Assert.That(ValueOf(shown, "Subject")).IsEqualTo("repo:x:*");
        await Assert.That(ValueOf(shown, "Claim")).IsEqualTo("env=prod");

        var replaced = (await Sform(InProject("account", "machine", "rules", machine.Id, "--issuer", "ci", "--audience", audience, "--subject", "sub-2", "--claim", "k=v")))
            .ShouldSucceed()
            .ShouldContain($"The federation rules of {machine.Id} have been replaced; they apply to new token exchanges.")
            .ShouldNotContain("repo:x:*");
        await Assert.That(ValueOf(replaced, "Subject")).IsEqualTo("sub-2");
        await Assert.That(ValueOf(replaced, "Claim")).IsEqualTo("k=v");
        (await Sform(first, "account")).ShouldExitWith(CliExit.Unauthorized);
        var second = AsMachine(await FederatedAsync(machine.Id, signer, "sub-2", Claim("k", "v")));
        (await Sform(second, "account")).ShouldSucceed();

        await WriteFileAsync("rules.json", RulesFile(Rule("ci", audience, "sub-3")));
        var fromFile = (await Sform(InProject("account", "machine", "rules", machine.Id, "--rules-file", "rules.json")))
            .ShouldSucceed()
            .ShouldContain($"The federation rules of {machine.Id} have been replaced; they apply to new token exchanges.")
            .ShouldNotContain("sub-2");
        await Assert.That(ValueOf(fromFile, "Subject")).IsEqualTo("sub-3");
        (await Sform(second, "account")).ShouldExitWith(CliExit.Unauthorized);
        (await Sform(AsMachine(await FederatedAsync(machine.Id, signer, "sub-3")), "account")).ShouldSucceed();
    }

    // MCH-FED-08: A token with a wrong subject, a missing claim or a lifetime above --max-lifetime is refused.
    // In a disposable project with the kinds ApiKey and Federated, the issuer ci of the test's key set with
    // --max-lifetime 600 --require-claim repo=a and a machine of account machine create -r --issuer ci --audience <token
    // endpoint> --subject repo:x:main: sform account as the machine ends with 44 for a token with another sub, one without
    // the repo claim, and one whose exp - iat is 900 seconds, while a token of the rule within 600 seconds signs in.
    [Test]
    [TestCase(360)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine issuer set", "--require-claim", "--max-lifetime")]
    [Covers("account machine create", "--subject")]
    public async Task Machine_FederatedWrongToken_Refused()
    {
        await CreateDisposableProjectAsync();
        await EnableCredentialKindsAsync("ApiKey,Federated");
        var signer = await CreateSignerAsync();
        (await Sform(InProject("account", "machine", "issuer", "set", "ci", "--issuer", TestIssuerUrl, "--jwks-file", JwksFile, "--max-lifetime", "600", "--require-claim", "repo=a")))
            .ShouldSucceed();
        var machine = await CreateMachineAsync("-r", "--issuer", "ci", "--audience", TokenEndpointOf(ProjectKey), "--subject", "repo:x:main");

        var wrongSubject = await FederatedAsync(machine.Id, signer, "repo:x:other", Claim("repo", "a"));
        var missingClaim = await FederatedAsync(machine.Id, signer, "repo:x:main");
        var tooLong = await FederatedAsync(machine.Id, signer, "repo:x:main", Claim("repo", "a"), TimeSpan.FromSeconds(900));
        foreach (var refused in new[] { wrongSubject, missingClaim, tooLong })
        {
            (await Sform(AsMachine(refused), "account")).ShouldExitWith(CliExit.Unauthorized);
        }

        (await Sform(AsMachine(await FederatedAsync(machine.Id, signer, "repo:x:main", Claim("repo", "a"))), "account")).ShouldSucceed();
    }

    // A rule of --rules-file, in the API's FederationRule shape: { "Issuer", "Audience", "Subject", "Claims": { k: [v] } }.
    private static JsonObject Rule(string issuer, string audience, string subject, params (string Name, string Value)[] claims)
    {
        var rule = new JsonObject { ["Issuer"] = issuer, ["Audience"] = audience, ["Subject"] = subject };
        if (claims.Length > 0)
        {
            var pins = new JsonObject();
            foreach (var (name, value) in claims)
            {
                var values = new JsonArray();
                values.Add(value);
                pins[name] = values;
            }

            rule["Claims"] = pins;
        }

        return rule;
    }

    private static string RulesFile(params JsonObject[] rules)
    {
        var array = new JsonArray();
        foreach (var rule in rules)
        {
            array.Add(rule);
        }

        return array.ToJsonString();
    }
}
