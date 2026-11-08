using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Policy and Encryption: sform story kv policy shows and changes the key/value policy of a project (the allowed levels,
// the default secret level, the mTLS rule, the HSM rotation, the customer key), rotates the platform's keys, and the
// levels byok and hsm that only a target with the feature offers. Every case but KV-POL-01 changes project-wide state, so
// it runs in a disposable project, which starts with the default policy: public and standard allowed, standard the
// default secret level, mTLS required for secret delivery, no customer key, no recipient.
[Area(Area.KeyValues)]
public sealed class KeyValuePolicyTests : CliTest
{
    // KV-POL-01: kv policy shows the allowed levels, the mTLS rule, the keys and the recipients.
    // As a Reader, kv policy prints the policy of the project: the allowed levels (public among them), the default secret
    // level, the mTLS rule, the version, and the platform's Standard key (its id and version) once the project has one.
    [Test]
    [TestCase(442)]
    [UserRole(Role.Reader)]
    [Covers("story kv policy")]
    public async Task KvPolicy_Show()
    {
        var policy = (await Sform(InProject("story", "kv", "policy"))).ShouldSucceed().JsonNode();

        await Assert.That(Levels(policy["AllowedProtections"])).Contains("Public");
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsNotNull();
        await Assert.That(policy["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsNotNull();
        await Assert.That(policy["Version"]?.GetValue<long>() ?? -1).IsGreaterThanOrEqualTo(0L);
        if (policy["Standard"] is JsonObject standard)
        {
            await Assert.That(standard["KeyId"]?.GetValue<string>()).IsNotNull();
            await Assert.That(standard["CurrentKeyVersion"]?.GetValue<string>()).IsNotNull();
        }
    }

    // KV-POL-02: policy set --allow --default --require-mtls --hsm-rotation-days changes the policy.
    // In a disposable project with a recipient (the enabled recipient that hyok needs): kv policy set --allow public
    // --allow standard --allow hyok --default standard --require-mtls false --hsm-rotation-days 30 says "The policy of
    // <org>.<project> is at version N." and kv policy shows the three levels, standard, mTLS off and 30 days; then
    // --require-mtls true turns mTLS on again, --default hyok and --default standard change the default secret level, each
    // keeping the members it does not give; kv policy set without an option ends with 2 and names the options.
    [Test]
    [TestCase(443)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv policy set", "--allow", "--default", "--require-mtls", "--hsm-rotation-days", "--allow=public", "--allow=standard", "--allow=hyok", "--default=standard", "--default=hyok", "--require-mtls=true", "--require-mtls=false")]
    public async Task KvPolicy_Set_Changes()
    {
        await CreateDisposableProjectAsync();
        var recipient = KeyValuesSupportB.NewRecipientKey();
        await WriteFileAsync("r.pem", recipient.PublicPem);
        (await Sform(InProject("story", "kv", "recipients", "add", "--name", CliData.Slug("rcp"), "--public-key", "r.pem"))).ShouldSucceed();

        var version = await SetPolicyAsync("--allow", "public", "--allow", "standard", "--allow", "hyok", "--default", "standard", "--require-mtls", "false", "--hsm-rotation-days", "30");
        var policy = await PolicyAsync(version);
        await Assert.That(Levels(policy["AllowedProtections"])).IsEquivalentTo(new[] { "Public", "Standard", "Hyok" });
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(policy["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsFalse();
        await Assert.That(policy["Hsm"]?["RotationInterval"]?.GetValue<string>()).IsEqualTo("30.00:00:00");

        version = await SetPolicyAsync("--require-mtls", "true");
        policy = await PolicyAsync(version);
        await Assert.That(policy["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsTrue();
        await Assert.That(Levels(policy["AllowedProtections"])).IsEquivalentTo(new[] { "Public", "Standard", "Hyok" });
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(policy["Hsm"]?["RotationInterval"]?.GetValue<string>()).IsEqualTo("30.00:00:00");

        version = await SetPolicyAsync("--default", "hyok");
        policy = await PolicyAsync(version);
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Hyok");
        await Assert.That(policy["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsTrue();

        version = await SetPolicyAsync("--default", "standard");
        policy = await PolicyAsync(version);
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(Levels(policy["AllowedProtections"])).IsEquivalentTo(new[] { "Public", "Standard", "Hyok" });

        (await Sform(InProject("story", "kv", "policy", "set")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Nothing to change; give at least one of --allow, --default, --require-mtls, --byok-key, --byok-tenant or --hsm-rotation-days.");
        await PolicyAsync(version);
    }

    // KV-POL-03: policy set --expected-version changes only at that version; a Contributor and a machine are refused.
    // In a disposable project whose policy moved on from version <old> (a change of the HSM rotation): kv policy set
    // --require-mtls false --expected-version <old> ends with 2 (PolicyVersionMismatch) and changes nothing, the same with
    // the current version turns mTLS off; kv policy set --require-mtls true as a Contributor (in <contributor project>,
    // or a Contributor granted in the disposable project when Target:RoleProjects maps none) and as a -w machine of the
    // project end with 44, and mTLS stays off.
    [Test]
    [TestCase(444)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv policy set", "--expected-version", "--require-mtls=false")]
    public async Task KvPolicy_ExpectedVersionAndRights()
    {
        await CreateDisposableProjectAsync();
        var machine = await CreateMachineAsync("-w");
        var old = (await PolicyAsync())["Version"]!.GetValue<long>();
        var current = await SetPolicyAsync("--hsm-rotation-days", "30");
        await Assert.That(current).IsEqualTo(old + 1);

        (await Sform(InProject("story", "kv", "policy", "set", "--require-mtls", "false", "--expected-version", $"{old}")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The policy is at version {current}, not {old}; reload and retry.")
            .ShouldContain("Error code: PolicyVersionMismatch");
        await Assert.That((await PolicyAsync(current))["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsTrue();

        var changed = await SetPolicyAsync("--require-mtls", "false", "--expected-version", $"{current}");
        await Assert.That(changed).IsEqualTo(current + 1);
        await Assert.That((await PolicyAsync(changed))["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsFalse();

        var roleProject = RoleProjectOf(Role.Contributor);
        var contributor = roleProject is null ? await AsGrantedUserAsync(PlatformRoles.Contributor) : null;
        var refused = contributor is null
            ? await Sform("story", "kv", "policy", "set", "--require-mtls", "true", "-p", roleProject!.Value.ProjectKey, "-v", roleProject.Value.View)
            : await contributor.Sform(InProject("story", "kv", "policy", "set", "--require-mtls", "true"));
        refused.ShouldExitWith(CliExit.Unauthorized);

        (await Sform(AsMachine(machine.Credentials!), "story", "kv", "policy", "set", "--require-mtls", "true"))
            .ShouldExitWith(CliExit.Unauthorized);

        await Assert.That((await PolicyAsync(changed))["RequireMtlsForSecretDelivery"]?.GetValue<bool>()).IsFalse();
    }

    // KV-POL-04: policy byok-setup prints the application to consent to and the role to grant; byok-verify without a
    // customer key fails with exit 2.
    // In a disposable project, kv policy byok-setup prints "Bring your own key" with the consent URL of the platform's
    // application, the role to grant its service principal, the commands to set and verify the key and the application
    // id; kv policy byok-verify, with no customer key in the policy, ends with 2, the server's message and "Error code:
    // ByokNotConfigured". The target has the platform's BYOK application (as the catalogue assumes), not necessarily a
    // customer key (the Byok feature).
    [Test]
    [TestCase(445)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv policy byok-setup")]
    [Covers("story kv policy byok-verify")]
    public async Task KvPolicy_ByokSetup_PrintsSteps()
    {
        await CreateDisposableProjectAsync();

        (await Sform(InProject("story", "kv", "policy", "byok-setup")))
            .ShouldSucceed()
            .ShouldContain("Bring your own key")
            .ShouldMatch(new Regex(@"consents to the platform's application: https://\S+"))
            .ShouldContain("Grant its service principal the role ")
            .ShouldContain("sform story kv policy set --byok-key")
            .ShouldContain("sform story kv policy byok-verify")
            .ShouldMatch(new Regex(@"Application id:\s*\S+"));

        (await Sform(InProject("story", "kv", "policy", "byok-verify")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The project has no customer key (BYOK) to verify.")
            .ShouldContain("Error code: ByokNotConfigured");
    }

    // KV-POL-05: --byok-key --byok-tenant set the customer key, byok-verify checks it, an empty --byok-key removes it.
    // With the target's customer key (KeyValues:ByokKeyUri and KeyValues:ByokTenantId of the settings), in a disposable
    // project: kv policy set --byok-key <uri> --byok-tenant <tenant> stores it (versionless); kv policy byok-verify says
    // "The customer key <uri> is Healthy (version …)"; kv audit --operation byokverify lists the ByokVerify event and no
    // other operation; kv policy set --byok-key "" removes the settings.
    [Test]
    [TestCase(446)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [RequiresFeature(CliFeatures.Byok)]
    [Covers("story kv policy set", "--byok-key", "--byok-tenant")]
    [Covers("story kv policy byok-verify")]
    [Covers("story kv audit", "--operation", "--operation=ByokVerify")]
    public async Task KvPolicy_Byok_SetVerifyRemove()
    {
        var (keyUri, tenant) = CustomerKey();
        await CreateDisposableProjectAsync();

        var version = await SetPolicyAsync("--byok-key", keyUri, "--byok-tenant", tenant);
        var policy = await PolicyAsync(version);
        await Assert.That(SameKey(policy["Byok"]?["KeyUri"]?.GetValue<string>(), keyUri)).IsTrue();
        await Assert.That(string.Equals(policy["Byok"]?["TenantId"]?.GetValue<string>(), tenant, StringComparison.OrdinalIgnoreCase)).IsTrue();

        (await Sform(InProject("story", "kv", "policy", "byok-verify")))
            .ShouldSucceed()
            .ShouldContain("The customer key ")
            .ShouldContain(" is Healthy (version ");

        var audit = (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", "byokverify"))).ShouldSucceed();
        var rows = KeyValuesSupportB.AuditRows(audit);
        await Assert.That(rows.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(rows.Select(row => row.Operation).Distinct()).IsEquivalentTo(new[] { "ByokVerify" });

        version = await SetPolicyAsync("--byok-key", string.Empty);
        await Assert.That((await PolicyAsync(version))["Byok"]).IsNull();
    }

    // KV-POL-06: policy rotate --protection standard rotates the key and the values stay readable.
    // In a disposable project with a standard secret: kv policy rotate --protection standard says "The Standard key is at
    // version N; the re-wrap of the stored values has started.", kv get <secret> --reveal still returns the value, and kv
    // policy rotate --protection byok ends with 2 and "Only standard and hsm keys are rotated by the platform.".
    [Test]
    [TestCase(447)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv policy rotate", "--protection", "--protection=standard")]
    public async Task KvPolicy_Rotate_KeepsValues()
    {
        await CreateDisposableProjectAsync();
        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--protection", "standard"))).ShouldSucceed();

        (await Sform(InProject("story", "kv", "policy", "rotate", "--protection", "standard")))
            .ShouldSucceed()
            .ShouldMatch(new Regex(@"The Standard key is at version \S+; the re-wrap of the stored values has started\."));

        await AssertRevealsAsync(key, secret);

        (await Sform(InProject("story", "kv", "policy", "rotate", "--protection", "byok")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Only standard and hsm keys are rotated by the platform.");
    }

    // KV-POL-07: BYOK: --allow byok --default byok, and kv set, protect, import, the list and the export at level byok.
    // With the customer key of KV-POL-05 set and verified in a disposable project, and a standard secret <k2>: kv policy
    // set --allow public --allow standard --allow byok --default byok (the allowed levels replace the stored ones, so
    // public and standard stay listed); kv set <k> --protection byok, kv protect <k2> --protection byok and kv import
    // b.env --format env --secret '<group>.B_*' --protection byok -y store the three secrets at level byok, each readable
    // with --reveal; kv --protection byok and kv export --protection byok hold only them; kv audit --operation reprotect
    // lists the Reprotect of <k2> and no other operation.
    [Test]
    [TestCase(448)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [RequiresFeature(CliFeatures.Byok)]
    [Covers("story kv policy set", "--allow=byok", "--default=byok")]
    [Covers("story kv set", "--protection", "--protection=byok")]
    [Covers("story kv protect", "--protection", "--protection=byok")]
    [Covers("story kv import", "--protection", "--protection=byok")]
    [Covers("story kv audit", "--operation", "--operation=Reprotect")]
    public async Task Kv_Byok_StoresAndMoves()
    {
        var (keyUri, tenant) = CustomerKey();
        await CreateDisposableProjectAsync();
        await SetPolicyAsync("--byok-key", keyUri, "--byok-tenant", tenant);
        (await Sform(InProject("story", "kv", "policy", "byok-verify"))).ShouldSucceed().ShouldContain(" is Healthy (version ");

        var policy = await PolicyAsync(await SetPolicyAsync("--allow", "public", "--allow", "standard", "--allow", "byok", "--default", "byok"));
        await Assert.That(Levels(policy["AllowedProtections"])).IsEquivalentTo(new[] { "Public", "Standard", "Byok" });
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Byok");

        await StoresAndMovesAsync("byok", "Byok");
    }

    // KV-POL-08: HSM: --allow hsm --default hsm, and kv set, protect, import, rotate, the list and the export at level hsm.
    // In a disposable project with a standard secret <k2>: kv policy set --allow public --allow standard --allow hsm
    // --default hsm (the allowed levels replace the stored ones); kv set <k> --protection hsm, kv protect <k2> --protection
    // hsm and kv import h.env --format env --secret '<group>.H_*' --protection hsm -y store the three secrets at level
    // hsm; kv policy rotate --protection hsm says "The Hsm key is at version N; the re-wrap of the stored values has
    // started." and the values stay readable with --reveal; kv --protection hsm and kv export --protection hsm hold only
    // them; kv audit --operation reprotect lists the Reprotect of <k2> and no other operation.
    [Test]
    [TestCase(449)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [RequiresFeature(CliFeatures.Hsm)]
    [Covers("story kv policy set", "--allow=hsm", "--default=hsm")]
    [Covers("story kv set", "--protection", "--protection=hsm")]
    [Covers("story kv protect", "--protection", "--protection=hsm")]
    [Covers("story kv import", "--protection", "--protection=hsm")]
    [Covers("story kv policy rotate", "--protection", "--protection=hsm")]
    [Covers("story kv audit", "--operation", "--operation=Reprotect")]
    public async Task Kv_Hsm_StoresMovesRotates()
    {
        await CreateDisposableProjectAsync();

        var policy = await PolicyAsync(await SetPolicyAsync("--allow", "public", "--allow", "standard", "--allow", "hsm", "--default", "hsm"));
        await Assert.That(Levels(policy["AllowedProtections"])).IsEquivalentTo(new[] { "Public", "Standard", "Hsm" });
        await Assert.That(policy["DefaultSecretProtection"]?.GetValue<string>()).IsEqualTo("Hsm");

        await StoresAndMovesAsync("hsm", "Hsm");
    }

    private static IReadOnlyList<string> Levels(JsonNode? levels)
    {
        return (levels as JsonArray)?.Select(level => level?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
    }

    private static bool SameKey(string? stored, string given)
    {
        return stored is not null && string.Equals(stored.TrimEnd('/'), given.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    // The steps KV-POL-07 and KV-POL-08 share once the level is allowed and the default: a secret set at the level, a
    // standard secret moved to it, a secret imported at it (with a public pair beside it), [hsm: the key rotated,] each
    // readable; the list and the export of the level hold only the three; the audit's reprotect lists the move only.
    private async Task StoresAndMovesAsync(string level, string levelName)
    {
        var group = CliData.KeyValueKey();
        var set = $"{group}.set";
        var moved = $"{group}.moved";
        var imported = $"{group}.{level[0]}_secret";
        var plain = $"{group}.plain";
        var setValue = CliData.SecretValue();
        var movedValue = CliData.SecretValue();
        var importedValue = CliData.SecretValue();
        AddSecret(setValue);
        AddSecret(movedValue);
        AddSecret(importedValue);
        string[] secrets = [setValue, movedValue, importedValue];

        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "set", moved, movedValue, "--protection", "standard"))).ShouldSucceed();

        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "set", set, setValue, "--protection", level)))
            .ShouldSucceed()
            .ShouldContain($"'{set}' is at version 1 ({levelName}).");

        (await Sform(InProject("story", "kv", "protect", moved, "--protection", level)))
            .ShouldSucceed()
            .ShouldContain("1 of 1 keys protected.");

        var envFile = $"{level[0]}.env";
        await WriteFileAsync(envFile, $"{KeyValuesSupportB.EnvName(imported)}={importedValue}\n{KeyValuesSupportB.EnvName(plain)}={CliData.Value()}\n");
        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "import", envFile, "--format", "env", "--secret", $"{group}.{level[0].ToString().ToUpperInvariant()}_*", "--protection", level, "-y")))
            .ShouldSucceed()
            .ShouldContain("Imported: 2 create.");

        if (level == "hsm")
        {
            (await Sform(InProject("story", "kv", "policy", "rotate", "--protection", "hsm")))
                .ShouldSucceed()
                .ShouldMatch(new Regex(@"The Hsm key is at version \S+; the re-wrap of the stored values has started\."));
        }

        foreach (var (key, value) in new[] { (set, setValue), (moved, movedValue), (imported, importedValue) })
        {
            var pair = await AssertRevealsAsync(key, value);
            await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo(levelName);
        }

        (await Sform(InProject("story", "kv", "--protection", level)))
            .ShouldSucceed()
            .ShouldContain(set)
            .ShouldContain(moved)
            .ShouldContain(imported)
            .ShouldNotContain(plain);

        var exported = KeyValuesSupportB.ItemsOf((await Sform(InProject("story", "kv", "export", "--protection", level))).ShouldSucceed().JsonNode());
        await Assert.That(exported.Keys).IsEquivalentTo(new[] { set, moved, imported });
        foreach (var item in exported.Values)
        {
            await Assert.That(item["Protection"]?.GetValue<string>()).IsEqualTo(levelName);
        }

        var audit = (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", "reprotect"))).ShouldSucceed();
        var rows = KeyValuesSupportB.AuditRows(audit);
        await Assert.That(rows.Select(row => row.Operation).Distinct()).IsEquivalentTo(new[] { "Reprotect" });
        await Assert.That(rows.Any(row => row.Line.Contains(moved, StringComparison.Ordinal))).IsTrue();
    }

    // kv get <key> --reveal returns the value; the pair.
    private async Task<JsonNode> AssertRevealsAsync(string key, string value)
    {
        var pair = (await Sform(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "get", key, "--reveal"))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
        return pair;
    }

    // kv policy set <options> succeeds with "The policy of <org>.<project> is at version N."; N.
    private async Task<long> SetPolicyAsync(params string[] options)
    {
        var result = (await Sform(InProject(["story", "kv", "policy", "set", .. options]))).ShouldSucceed();
        var version = result.JsonNode()["Version"]?.GetValue<long>() ?? throw result.Fail("printed a policy without a Version");
        result.ShouldContain($"The policy of {ProjectKey} is at version {version}.");
        return version;
    }

    // kv policy of the acting project, at the version when one is given.
    private async Task<JsonNode> PolicyAsync(long? version = null)
    {
        var policy = (await Sform(InProject("story", "kv", "policy"))).ShouldSucceed().JsonNode();
        if (version is { } expected)
        {
            await Assert.That(policy["Version"]?.GetValue<long>()).IsEqualTo(expected);
        }

        return policy;
    }

    // The target's customer key of the Byok feature: KeyValues:ByokKeyUri and KeyValues:ByokTenantId (KeyValueSettings),
    // which CliAutomationSetup.Validate requires when Target:Features lists Byok.
    private (string KeyUri, string TenantId) CustomerKey()
    {
        var settings = Settings.Bind<KeyValueSettings>(KeyValueSettings.Section);
        if (string.IsNullOrWhiteSpace(settings.ByokKeyUri) || string.IsNullOrWhiteSpace(settings.ByokTenantId))
        {
            throw new InvalidOperationException(KeyValueSettings.MissingCustomerKey);
        }

        return (settings.ByokKeyUri.Trim(), settings.ByokTenantId.Trim());
    }
}
