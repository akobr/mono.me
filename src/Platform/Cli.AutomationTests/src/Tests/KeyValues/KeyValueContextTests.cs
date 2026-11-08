using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Context: every key/value command targets the project of --projectKey, and the view of -v or --view (KV-CTX-01 the
// commands of a view, KV-CTX-02 those of the project). <A> is the target project, <B> a disposable project of the test,
// <view> a new view of B (a view exists by its data); the test does not act in B (UseProject), so every command of the
// steps names it itself.
[Area(Area.KeyValues)]
public sealed class KeyValueContextTests : CliTest
{
    // KV-CTX-01: Every key-value command of a view takes its project from --projectKey and its view from -v or --view.
    // In <view> of B, each command runs twice, once with --projectKey <B> --view <view> and once with --projectKey <B> -v
    // <view>: kv set of two public pairs and two secrets, kv (the listing), kv get, kv versions, kv diff, kv protect
    // --protection standard of the secrets (Unchanged: they are standard already), kv export -o --include-secrets, kv import
    // -y into <view2> and <view3>, kv delete and kv restore of the public pairs, kv delete and kv purge -y of the secrets;
    // then, with a recipient in B and hyok allowed, kv set of a hyok value (--view), a second recipient, and kv reencrypt of
    // it with the first recipient's private key. Each acts on the pairs of <view> in B: they are listed, versioned, found by
    // kv protect, exported with their values, deleted, restored and purged there, the imports bring them into <view2> and
    // <view3>, and nothing appears in A or in the view default of B.
    [Test]
    [TestCase(461)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv set", "--projectKey", "-v", "--view")]
    [Covers("story kv", "--projectKey", "-v", "--view")]
    [Covers("story kv get", "--projectKey", "-v", "--view")]
    [Covers("story kv versions", "--projectKey", "-v", "--view")]
    [Covers("story kv diff", "--projectKey", "-v", "--view")]
    [Covers("story kv protect", "--projectKey", "-v", "--view")]
    [Covers("story kv export", "--projectKey", "-v", "--view")]
    [Covers("story kv import", "--projectKey", "-v", "--view")]
    [Covers("story kv delete", "--projectKey", "-v", "--view")]
    [Covers("story kv restore", "--projectKey", "-v", "--view")]
    [Covers("story kv purge", "--projectKey", "-v", "--view")]
    [Covers("story kv reencrypt", "--projectKey", "-v", "--view")]
    public async Task KvCommands_ContextOptions_TargetProjectAndView()
    {
        var projectB = await CreateDisposableProjectAsync(actInIt: false);
        var view = CliData.ViewName();
        var view2 = CliData.ViewName();
        var view3 = CliData.ViewName();
        var root = CliData.KeyValueKey();
        var (k1, k2, s1, s2, h1) = ($"{root}.k1", $"{root}.k2", $"{root}.s1", $"{root}.s2", $"{root}.h1");
        var (value1, value2) = (CliData.Value(), CliData.Value());
        var (secret1, secret2) = (CliData.SecretValue(), CliData.SecretValue());
        AddSecret(secret1);
        AddSecret(secret2);
        var secrets = new SformRunOptions { Secrets = [secret1, secret2] };
        string[] Long(params string[] arguments) => [.. arguments, "--projectKey", projectB, "--view", view];
        string[] Short(params string[] arguments) => [.. arguments, "--projectKey", projectB, "-v", view];

        (await Sform(Long("story", "kv", "set", k1, value1, "--protection", "public"))).ShouldSucceed().ShouldContain($"'{k1}' is at version 1 (Public).");
        (await Sform(Short("story", "kv", "set", k2, value2, "--protection", "public"))).ShouldSucceed().ShouldContain($"'{k2}' is at version 1 (Public).");
        (await Sform(secrets, Long("story", "kv", "set", s1, secret1, "--secret"))).ShouldSucceed().ShouldContain($"'{s1}' is at version 1 (");
        (await Sform(secrets, Short("story", "kv", "set", s2, secret2, "--secret"))).ShouldSucceed().ShouldContain($"'{s2}' is at version 1 (");

        foreach (var listing in new[] { Long("story", "kv"), Short("story", "kv") })
        {
            var listed = (await Sform(listing)).ShouldSucceed().ShouldContain($"Key/value pairs of {projectB} / {view}");
            await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(listed, root))).IsEquivalentTo(Sorted([k1, k2, s1, s2]));
        }

        var got1 = (await Sform(Long("story", "kv", "get", k1))).ShouldSucceed().JsonNode();
        await Assert.That(got1["Value"]?.GetValue<string>()).IsEqualTo(value1);
        var got2 = (await Sform(Short("story", "kv", "get", k2))).ShouldSucceed().JsonNode();
        await Assert.That(got2["Value"]?.GetValue<string>()).IsEqualTo(value2);

        (await Sform(Long("story", "kv", "versions", k1))).ShouldSucceed().ShouldContain($"Versions of '{k1}'").ShouldContain("1 (current)");
        (await Sform(Short("story", "kv", "versions", k2))).ShouldSucceed().ShouldContain($"Versions of '{k2}'").ShouldContain("1 (current)");

        (await Sform(Long("story", "kv", "diff", k1))).ShouldSucceed().ShouldContain($"+ {value1}");
        (await Sform(Short("story", "kv", "diff", k2))).ShouldSucceed().ShouldContain($"+ {value2}");

        foreach (var (key, arguments) in new[] { (s1, Long("story", "kv", "protect", s1, "--protection", "standard")), (s2, Short("story", "kv", "protect", s2, "--protection", "standard")) })
        {
            var protect = (await Sform(arguments)).ShouldSucceed().ShouldContain("1 of 1 keys protected.");
            await Assert.That(KeyValuesSupportA.RowOf(protect, key)).Contains("Unchanged");
        }

        (await Sform(Long("story", "kv", "export", "-o", "b1.json", "--include-secrets"))).ShouldSucceed().ShouldContain("4 pair(s) written to b1.json.");
        (await Sform(Short("story", "kv", "export", "-o", "b2.json", "--include-secrets"))).ShouldSucceed().ShouldContain("4 pair(s) written to b2.json.");
        foreach (var file in new[] { "b1.json", "b2.json" })
        {
            var exported = (JsonNode.Parse(await ReadFileAsync(file))!["Items"] as JsonArray ?? new JsonArray()).ToDictionary(
                item => item!["Key"]!.GetValue<string>(),
                item => item!["Value"]?.GetValue<string>(),
                StringComparer.Ordinal);
            await Assert.That(Sorted(exported.Keys)).IsEquivalentTo(Sorted([k1, k2, s1, s2]));
            await Assert.That(exported[s1]).IsEqualTo(secret1);
            await Assert.That(exported[k2]).IsEqualTo(value2);
        }

        (await Sform(secrets, "story", "kv", "import", "b1.json", "-y", "--projectKey", projectB, "--view", view2)).ShouldSucceed().ShouldContain("Imported: ");
        (await Sform(secrets, "story", "kv", "import", "b2.json", "-y", "--projectKey", projectB, "-v", view3)).ShouldSucceed().ShouldContain("Imported: ");

        (await Sform(Long("story", "kv", "delete", k1))).ShouldSucceed().ShouldContain($"'{k1}' was deleted.");
        (await Sform(Short("story", "kv", "delete", k2))).ShouldSucceed().ShouldContain($"'{k2}' was deleted.");
        (await Sform(Long("story", "kv", "restore", k1))).ShouldSucceed().ShouldContain("1 of 1 keys restored.");
        (await Sform(Short("story", "kv", "restore", k2))).ShouldSucceed().ShouldContain("1 of 1 keys restored.");
        (await Sform(Long("story", "kv", "delete", s1))).ShouldSucceed().ShouldContain($"'{s1}' was deleted.");
        (await Sform(Short("story", "kv", "delete", s2))).ShouldSucceed().ShouldContain($"'{s2}' was deleted.");
        (await Sform(Long("story", "kv", "purge", s1, "-y"))).ShouldSucceed().ShouldContain("1 of 1 keys purged.");
        (await Sform(Short("story", "kv", "purge", s2, "-y"))).ShouldSucceed().ShouldContain("1 of 1 keys purged.");

        // hyok: a recipient of B, the level allowed, a value encrypted for it, a second recipient, the value encrypted again
        var (publicA, privateA) = KeyValuesSupportA.NewRsaKeyPair();
        var (publicD, _) = KeyValuesSupportA.NewRsaKeyPair();
        await WriteFileAsync("a.pem", publicA);
        await WriteFileAsync("a.key.pem", privateA);
        await WriteFileAsync("d.pem", publicD);
        var recipientA = KeyValuesSupportA.RecipientIdOf(
            (await Sform("story", "kv", "recipients", "add", "--name", "a", "--public-key", "a.pem", "--projectKey", projectB)).ShouldSucceed(),
            "a");
        (await Sform("story", "kv", "policy", "set", "--allow", "public", "--allow", "standard", "--allow", "hyok", "--projectKey", projectB)).ShouldSucceed();

        var hyokValue = CliData.Value();
        var hyok = await KeyValuesSupportA.EventuallyAsync(() => Sform(Long("story", "kv", "set", h1, hyokValue, "--protection", "hyok")), result => result.Succeeded);
        hyok.ShouldSucceed()
            .ShouldContain("Encrypted locally for 1 recipient(s); the platform cannot read the value.")
            .ShouldContain($"'{h1}' is at version 1 (Hyok).");

        // a write at hyok checks the API's cached policy too, so each re-encryption goes again until hyok is allowed there
        (await Sform("story", "kv", "recipients", "add", "--name", "d", "--public-key", "d.pem", "--projectKey", projectB)).ShouldSucceed();
        foreach (var arguments in new[] { Long("story", "kv", "reencrypt", h1, "--private-key", "a.key.pem", "--kid", recipientA), Short("story", "kv", "reencrypt", h1, "--private-key", "a.key.pem", "--kid", recipientA) })
        {
            (await KeyValuesSupportA.EventuallyAsync(() => Sform(arguments), result => result.Succeeded))
                .ShouldSucceed()
                .ShouldContain("1 value(s) encrypted again for 2 recipient(s).");
        }

        // where the pairs are: <view> of B (the purged secrets gone), the imports in <view2> and <view3>, nothing elsewhere
        var inView = (await Sform("story", "kv", "--all", "-p", projectB, "-v", view)).ShouldSucceed();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(inView, root))).IsEquivalentTo(Sorted([k1, k2, h1]));
        foreach (var imported in new[] { view2, view3 })
        {
            var listed = (await Sform("story", "kv", "-p", projectB, "-v", imported)).ShouldSucceed();
            await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(listed, root))).IsEquivalentTo(Sorted([k1, k2, s1, s2]));
        }

        foreach (var (projectKey, elsewhere) in new[] { (projectB, "default"), (Target.ProjectKey, view), (Target.ProjectKey, Target.View) })
        {
            var listed = (await Sform("story", "kv", "--all", "--prefix", root, "-p", projectKey, "-v", elsewhere)).ShouldSucceed();
            await Assert.That(KeyValuesSupportA.ListedKeys(listed, root)).IsEmpty();
        }
    }

    // KV-CTX-02: The policy, recipient, audit and usage commands take their project from --projectKey; -v and --view change
    // only the audit.
    // A secret in <view> of B and one in the view default of B (each audited). Each command runs with --projectKey <B> and
    // --view <view>, and with -v <view>: kv policy, kv policy set --require-mtls false then true, kv policy byok-setup, kv
    // policy byok-verify (no customer key: exit 2 and "Error code: ByokNotConfigured"), kv policy rotate --protection
    // standard, kv recipients add of r1 and r2, kv recipients, kv recipients disable, enable and remove -y of each, kv audit
    // and kv usage. Each acts on B: its policy changes and its key rotates, the recipients, the audit events and the usage
    // are B's; the view changes nothing for the policy (both reads are the same version) and the recipients, while kv audit
    // lists only the events of <view>. A's policy (the mTLS rule, the Standard key's version, the recipients) stays as it
    // was.
    [Test]
    [TestCase(462)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv policy", "--projectKey", "-v", "--view")]
    [Covers("story kv policy set", "--projectKey", "-v", "--view")]
    [Covers("story kv policy byok-setup", "--projectKey", "-v", "--view")]
    [Covers("story kv policy byok-verify", "--projectKey", "-v", "--view")]
    [Covers("story kv policy rotate", "--projectKey", "-v", "--view")]
    [Covers("story kv recipients", "--projectKey", "-v", "--view")]
    [Covers("story kv recipients add", "--projectKey", "-v", "--view")]
    [Covers("story kv recipients disable", "--projectKey", "-v", "--view")]
    [Covers("story kv recipients enable", "--projectKey", "-v", "--view")]
    [Covers("story kv recipients remove", "--projectKey", "-v", "--view")]
    [Covers("story kv audit", "--projectKey", "-v", "--view")]
    [Covers("story kv usage", "--projectKey", "-v", "--view")]
    public async Task KvProjectCommands_ContextOptions_TargetProject()
    {
        var policyOfA = await PolicyOfAsync(Target.ProjectKey);
        var projectB = await CreateDisposableProjectAsync(actInIt: false);
        var view = CliData.ViewName();
        var root = CliData.KeyValueKey();
        var inView = $"{root}.inview";
        var inDefault = $"{root}.indefault";
        var (secret1, secret2) = (CliData.SecretValue(), CliData.SecretValue());
        AddSecret(secret1);
        AddSecret(secret2);
        (await Sform(new SformRunOptions { Secrets = [secret1] }, "story", "kv", "set", inView, secret1, "--secret", "-p", projectB, "-v", view)).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret2] }, "story", "kv", "set", inDefault, secret2, "--secret", "-p", projectB, "-v", "default")).ShouldSucceed();
        string[] Long(params string[] arguments) => [.. arguments, "--projectKey", projectB, "--view", view];
        string[] Short(params string[] arguments) => [.. arguments, "--projectKey", projectB, "-v", view];

        var policyLong = (await Sform(Long("story", "kv", "policy"))).ShouldSucceed().JsonNode();
        var policyShort = (await Sform(Short("story", "kv", "policy"))).ShouldSucceed().JsonNode();
        await Assert.That(policyShort["Version"]?.ToJsonString()).IsEqualTo(policyLong["Version"]?.ToJsonString());

        var mtlsOff = (await Sform(Long("story", "kv", "policy", "set", "--require-mtls", "false")))
            .ShouldSucceed()
            .ShouldContain($"The policy of {projectB} is at version ");
        await Assert.That(mtlsOff.JsonNode()["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsFalse();
        var mtlsOn = (await Sform(Short("story", "kv", "policy", "set", "--require-mtls", "true")))
            .ShouldSucceed()
            .ShouldContain($"The policy of {projectB} is at version ");
        await Assert.That(mtlsOn.JsonNode()["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsTrue();

        foreach (var arguments in new[] { Long("story", "kv", "policy", "byok-setup"), Short("story", "kv", "policy", "byok-setup") })
        {
            (await Sform(arguments)).ShouldSucceed().ShouldContain("Bring your own key");
        }

        foreach (var arguments in new[] { Long("story", "kv", "policy", "byok-verify"), Short("story", "kv", "policy", "byok-verify") })
        {
            (await Sform(arguments)).ShouldExitWith(CliExit.WrongInput).ShouldContain("Error code: ByokNotConfigured");
        }

        foreach (var arguments in new[] { Long("story", "kv", "policy", "rotate", "--protection", "standard"), Short("story", "kv", "policy", "rotate", "--protection", "standard") })
        {
            (await Sform(arguments)).ShouldSucceed().ShouldContain("The Standard key is at version ").ShouldContain("the re-wrap of the stored values has started.");
        }

        var (public1, _) = KeyValuesSupportA.NewRsaKeyPair();
        var (public2, _) = KeyValuesSupportA.NewRsaKeyPair();
        await WriteFileAsync("r1.pem", public1);
        await WriteFileAsync("r2.pem", public2);
        var r1 = KeyValuesSupportA.RecipientIdOf((await Sform(Long("story", "kv", "recipients", "add", "--name", "r1", "--public-key", "r1.pem"))).ShouldSucceed(), "r1");
        var r2 = KeyValuesSupportA.RecipientIdOf((await Sform(Short("story", "kv", "recipients", "add", "--name", "r2", "--public-key", "r2.pem"))).ShouldSucceed(), "r2");

        foreach (var arguments in new[] { Long("story", "kv", "recipients"), Short("story", "kv", "recipients") })
        {
            var recipients = (await Sform(arguments)).ShouldSucceed().ShouldContain($"Recipients of {projectB}");
            await Assert.That(KeyValuesSupportA.Lists(recipients, r1)).IsTrue();
            await Assert.That(KeyValuesSupportA.Lists(recipients, r2)).IsTrue();
        }

        (await Sform(Long("story", "kv", "recipients", "disable", r1))).ShouldSucceed().ShouldContain($"Recipient '{r1}' is disabled.");
        (await Sform(Short("story", "kv", "recipients", "disable", r2))).ShouldSucceed().ShouldContain($"Recipient '{r2}' is disabled.");
        (await Sform(Long("story", "kv", "recipients", "enable", r1))).ShouldSucceed().ShouldContain($"Recipient '{r1}' is enabled.");
        (await Sform(Short("story", "kv", "recipients", "enable", r2))).ShouldSucceed().ShouldContain($"Recipient '{r2}' is enabled.");
        (await Sform(Long("story", "kv", "recipients", "remove", r1, "-y"))).ShouldSucceed().ShouldContain($"Recipient '{r1}' removed.");
        (await Sform(Short("story", "kv", "recipients", "remove", r2, "-y"))).ShouldSucceed().ShouldContain($"Recipient '{r2}' removed.");

        foreach (var arguments in new[] { Long("story", "kv", "audit"), Short("story", "kv", "audit") })
        {
            (await Sform(arguments))
                .ShouldSucceed()
                .ShouldContain($"Secret audit trail of {projectB}")
                .ShouldContain($"{view}/{inView}")
                .ShouldNotContain(inDefault);
        }

        foreach (var arguments in new[] { Long("story", "kv", "usage"), Short("story", "kv", "usage") })
        {
            (await Sform(arguments)).ShouldSucceed().ShouldContain("Key/value usage ");
        }

        var policyOfB = await PolicyOfAsync(projectB);
        await Assert.That(policyOfB["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsTrue();
        await Assert.That(RecipientIds(policyOfB)).IsEmpty();

        var policyOfAAfter = await PolicyOfAsync(Target.ProjectKey);
        await Assert.That(policyOfAAfter["RequireMtlsForSecretDelivery"]?.ToJsonString()).IsEqualTo(policyOfA["RequireMtlsForSecretDelivery"]?.ToJsonString());
        await Assert.That(policyOfAAfter["Standard"]?["CurrentKeyVersion"]?.ToJsonString()).IsEqualTo(policyOfA["Standard"]?["CurrentKeyVersion"]?.ToJsonString());
        await Assert.That(Sorted(RecipientIds(policyOfAAfter))).IsEquivalentTo(Sorted(RecipientIds(policyOfA)));
    }

    private static string[] Sorted(IEnumerable<string> values)
    {
        return values.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] RecipientIds(JsonNode policy)
    {
        return (policy["Recipients"] as JsonArray ?? new JsonArray()).Select(recipient => recipient?["Id"]?.GetValue<string>() ?? string.Empty).ToArray();
    }

    // The key/value policy of the project (sform story kv policy -p <project>), as the JSON sform prints.
    private async Task<JsonNode> PolicyOfAsync(string projectKey)
    {
        return (await Sform(new SformRunOptions { Label = $"the key/value policy of {projectKey}" }, "story", "kv", "policy", "-p", projectKey)).ShouldSucceed().JsonNode();
    }
}
