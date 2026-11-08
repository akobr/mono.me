using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Export and Import: sform story kv export writes the pairs of a view as a key/value document (json), a flat JSON object
// or .env lines, and kv import writes such a file back after showing its plan. The keys of a test share a generated
// group (cli_<run>.<word>_<hex>), and every export of the target project names it with --prefix, so a run reads only its
// own pairs (and never reveals another test's secrets).
[Area(Area.KeyValues)]
public sealed class KeyValueTransferTests : CliTest
{
    // KV-TRN-01: kv export writes a json document to stdout, and -o to a file.
    // An Editor stores two public pairs of a group; as a Reader, kv export --prefix <group>. prints the key/value document
    // (2s.key-values.v1) with both pairs and their values, and so does --format json; -o kv.json and --output out/kv.json
    // write the same document into the files and say how many pairs they wrote.
    [Test]
    [TestCase(413)]
    [UserRole(Role.Reader)]
    [Covers("story kv export", "-o", "--output", "--format=json")]
    public async Task KvExport_JsonDocument()
    {
        var editor = await ArrangeAsEditorAsync();
        var group = CliData.KeyValueKey();
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{group}.alpha"] = CliData.Value(),
            [$"{group}.beta"] = CliData.Value(),
        };
        foreach (var (key, value) in pairs)
        {
            (await editor.Sform(InProject("story", "kv", "set", key, value, "--protection", "public"))).ShouldSucceed();
        }

        var prefix = $"{group}.";
        await AssertDocumentAsync((await Sform(InProject("story", "kv", "export", "--prefix", prefix))).ShouldSucceed().JsonNode(), pairs);
        await AssertDocumentAsync((await Sform(InProject("story", "kv", "export", "--prefix", prefix, "--format", "json"))).ShouldSucceed().JsonNode(), pairs);

        (await Sform(InProject("story", "kv", "export", "--prefix", prefix, "-o", "kv.json")))
            .ShouldSucceed()
            .ShouldContain("2 pair(s) written to kv.json.");
        await AssertDocumentAsync(JsonNode.Parse(await ReadFileAsync("kv.json")), pairs);

        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(WorkingDirectory, "out"));
        var output = FileSystem.Path.Combine("out", "kv.json");
        (await Sform(InProject("story", "kv", "export", "--prefix", prefix, "--output", output)))
            .ShouldSucceed()
            .ShouldContain($"2 pair(s) written to {output}.");
        await AssertDocumentAsync(JsonNode.Parse(await ReadFileAsync(output)), pairs);
    }

    // KV-TRN-02: --format flat and env, and the format guessed from the file name.
    // An Editor stores two public pairs of a group; as a Reader, kv export --prefix <group>. --format flat prints one flat
    // JSON object of key and value, --format env prints KEY=VALUE lines (the key upper-cased, '.' as "__"), and -o kv.env
    // without --format writes the same .env lines, the format guessed from the file name.
    [Test]
    [TestCase(414)]
    [UserRole(Role.Reader)]
    [Covers("story kv export", "--format", "--format=flat", "--format=env")]
    public async Task KvExport_FlatAndEnv()
    {
        var editor = await ArrangeAsEditorAsync();
        var group = CliData.KeyValueKey();
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{group}.alpha"] = CliData.Value(),
            [$"{group}.beta"] = CliData.Value(),
        };
        foreach (var (key, value) in pairs)
        {
            (await editor.Sform(InProject("story", "kv", "set", key, value, "--protection", "public"))).ShouldSucceed();
        }

        var prefix = $"{group}.";
        var flat = (await Sform(InProject("story", "kv", "export", "--prefix", prefix, "--format", "flat"))).ShouldSucceed();
        var flatObject = flat.JsonNode().AsObject();
        await Assert.That(flatObject.ContainsKey("Items")).IsFalse();
        await Assert.That(flatObject.Select(property => property.Key)).IsEquivalentTo(pairs.Keys);
        foreach (var (key, value) in pairs)
        {
            await Assert.That(flatObject[key]?.GetValue<string>()).IsEqualTo(value);
        }

        var env = (await Sform(InProject("story", "kv", "export", "--prefix", prefix, "--format", "env"))).ShouldSucceed();
        await AssertEnvAsync(KeyValuesSupportB.EnvEntries(env.PlainStdOut), pairs);

        (await Sform(InProject("story", "kv", "export", "--prefix", prefix, "-o", "kv.env")))
            .ShouldSucceed()
            .ShouldContain("2 pair(s) written to kv.env.");
        var file = await ReadFileAsync("kv.env");
        await Assert.That(file.TrimStart()).DoesNotStartWith("{");
        await AssertEnvAsync(KeyValuesSupportB.EnvEntries(file), pairs);
    }

    // KV-TRN-03: --prefix and --protection limit the export.
    // A ContributorWithSecrets stores a public pair and a standard secret under <group>.app. and a public pair under
    // <group>.other.; as a Reader, kv export --prefix <group>.app. has only the two app keys, adding --protection public
    // --protection standard keeps both, and kv export --prefix <group>. --protection hsm --protection byok
    // --protection hyok has none of the group's public and standard keys.
    [Test]
    [TestCase(415)]
    [UserRole(Role.Reader)]
    [Covers("story kv export", "--prefix", "--protection", "--protection=public", "--protection=standard", "--protection=hsm", "--protection=byok", "--protection=hyok")]
    public async Task KvExport_Filters()
    {
        // The pairs are arranged where the Reader reads, by a ContributorWithSecrets of Target:Project (a standard secret
        // needs secret rights), as ArrangeAsEditorAsync does for an Editor.
        if (RoleProject is { } mapped)
        {
            throw new InvalidOperationException(
                $"The test arranges a secret in the Reader's project, and Target:RoleProjects:{ActingRole} maps it to '{mapped}', where every pool user holds {ActingRole}; leave the role unmapped (a {ActingRole} of Target:Project acts).");
        }

        var writer = await AsAnotherUserAsync(SecretsUser);
        var group = CliData.KeyValueKey();
        var appPublic = $"{group}.app.alpha";
        var appSecret = $"{group}.app.beta";
        var other = $"{group}.other.gamma";
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await writer.Sform(InProject("story", "kv", "set", appPublic, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await writer.Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", appSecret, secret, "--protection", "standard"))).ShouldSucceed();
        (await writer.Sform(InProject("story", "kv", "set", other, CliData.Value(), "--protection", "public"))).ShouldSucceed();

        var app = KeyValuesSupportB.ItemsOf((await Sform(InProject("story", "kv", "export", "--prefix", $"{group}.app."))).ShouldSucceed().JsonNode());
        await Assert.That(app.Keys).IsEquivalentTo(new[] { appPublic, appSecret });

        var levels = KeyValuesSupportB.ItemsOf(
            (await Sform(InProject("story", "kv", "export", "--prefix", $"{group}.app.", "--protection", "public", "--protection", "standard"))).ShouldSucceed().JsonNode());
        await Assert.That(levels.Keys).IsEquivalentTo(new[] { appPublic, appSecret });
        await Assert.That(levels[appPublic]["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        await Assert.That(levels[appSecret]["Protection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(levels[appSecret]["Value"]).IsNull();

        var none = (await Sform(InProject("story", "kv", "export", "--prefix", $"{group}.", "--protection", "hsm", "--protection", "byok", "--protection", "hyok")))
            .ShouldSucceed();
        await Assert.That(KeyValuesSupportB.ItemsOf(none.JsonNode()).Count).IsEqualTo(0);
        none.ShouldNotContain(appPublic).ShouldNotContain(appSecret).ShouldNotContain(other);
    }

    // KV-TRN-04: Secrets have no value without --include-secrets; a Contributor exports without them and is refused
    // --include-secrets.
    // As a ContributorWithSecrets, a public pair and a secret of a group: kv export --prefix <group>. lists the secret
    // without its value, kv export --include-secrets with it. As a Contributor (in <contributor project>, or a Contributor
    // of the target project when Target:RoleProjects maps none, reading the same group), kv export works and lists the
    // secrets without values, and kv export --include-secrets ends with 44 and "The caller has no rights to export
    // secrets.".
    [Test]
    [TestCase(416)]
    [UserRole(Role.Secrets)]
    [Covers("story kv export", "--include-secrets")]
    public async Task KvExport_IncludeSecrets()
    {
        var group = CliData.KeyValueKey();
        var plain = $"{group}.plain";
        var hidden = $"{group}.hidden";
        var plainValue = CliData.Value();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(InProject("story", "kv", "set", plain, plainValue, "--protection", "public"))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", hidden, secret, "--secret"))).ShouldSucceed();

        var withoutValues = (await Sform(InProject("story", "kv", "export", "--prefix", $"{group}."))).ShouldSucceed().ShouldNotContain(secret);
        var listed = KeyValuesSupportB.ItemsOf(withoutValues.JsonNode());
        await Assert.That(listed.Keys).IsEquivalentTo(new[] { plain, hidden });
        await Assert.That(listed[plain]["Value"]?.GetValue<string>()).IsEqualTo(plainValue);
        await Assert.That(listed[hidden]["Value"]).IsNull();

        var withValues = KeyValuesSupportB.ItemsOf(
            (await Sform(InProject("story", "kv", "export", "--prefix", $"{group}.", "--include-secrets"))).ShouldSucceed().JsonNode());
        await Assert.That(withValues[hidden]["Value"]?.GetValue<string>()).IsEqualTo(secret);
        await Assert.That(withValues[plain]["Value"]?.GetValue<string>()).IsEqualTo(plainValue);

        // As a Contributor: the role's project of Target:RoleProjects, else a Contributor of the target project, who reads
        // this test's group there (the secret listed without its value).
        string[]? roleProject = RoleProjectOf(Role.Contributor) is { } mapped ? ["-p", mapped.ProjectKey, "-v", mapped.View] : null;
        var contributor = roleProject is null ? await AsAnotherUserAsync(PlatformRoles.RequirementOf(Role.Contributor)) : null;
        string[] scope = roleProject ?? ["-p", ProjectKey, "-v", View, "--prefix", $"{group}."];
        Task<SformResult> AsContributor(params string[] arguments) => contributor is null ? Sform(arguments) : contributor.Sform(arguments);

        var contributorExport = (await AsContributor(["story", "kv", "export", .. scope])).ShouldSucceed().ShouldNotContain(secret);
        foreach (var item in KeyValuesSupportB.ItemsOf(contributorExport.JsonNode()).Values)
        {
            if (item["Protection"]?.GetValue<string>() is { } level && level != "Public")
            {
                await Assert.That(item["Value"]).IsNull();
            }
        }

        if (contributor is not null)
        {
            await Assert.That(KeyValuesSupportB.ItemsOf(contributorExport.JsonNode()).ContainsKey(hidden)).IsTrue();
        }

        (await AsContributor(["story", "kv", "export", "--include-secrets", .. scope]))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("The caller has no rights to export secrets.")
            .ShouldNotContain(secret);
    }

    // KV-TRN-05: kv import -y merges a json document.
    // As an Editor, a pair of a group that no file lists; kv import kv.json -y (two new public pairs) says "Imported: 2
    // create." and writes them, kv import kv2.json --format json --yes writes its pair, and the other pair keeps its value
    // and version.
    [Test]
    [TestCase(426)]
    [UserRole(Role.Editor)]
    [Covers("story kv import", "arg0", "-y", "--yes", "--format=json")]
    public async Task KvImport_MergeJson()
    {
        var group = CliData.KeyValueKey();
        var untouched = $"{group}.untouched";
        var untouchedValue = CliData.Value();
        (await Sform(InProject("story", "kv", "set", untouched, untouchedValue, "--protection", "public"))).ShouldSucceed();

        var first = new Dictionary<string, string>(StringComparer.Ordinal) { [$"{group}.alpha"] = CliData.Value(), [$"{group}.beta"] = CliData.Value() };
        var second = new Dictionary<string, string>(StringComparer.Ordinal) { [$"{group}.gamma"] = CliData.Value() };
        await WriteFileAsync("kv.json", KeyValuesSupportB.Document([.. first.Select(pair => (pair.Key, "Public", pair.Value))]));
        await WriteFileAsync("kv2.json", KeyValuesSupportB.Document([.. second.Select(pair => (pair.Key, "Public", pair.Value))]));

        (await Sform(InProject("story", "kv", "import", "kv.json", "-y")))
            .ShouldSucceed()
            .ShouldContain("Plan: 2 create.")
            .ShouldContain("Imported: 2 create.");
        (await Sform(InProject("story", "kv", "import", "kv2.json", "--format", "json", "--yes")))
            .ShouldSucceed()
            .ShouldContain("Imported: 1 create.");

        foreach (var (key, value) in first.Concat(second))
        {
            var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
            await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
            await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        }

        var kept = (await Sform(InProject("story", "kv", "get", untouched))).ShouldSucceed().JsonNode();
        await Assert.That(kept["Value"]?.GetValue<string>()).IsEqualTo(untouchedValue);
        await Assert.That(kept["Version"]?.GetValue<int>()).IsEqualTo(1);
    }

    // KV-TRN-06: --format flat and env imports, --secret globs and --protection for the secrets.
    // As a ContributorWithSecrets: kv import a.flat.json --format flat --secret '*_TOKEN' --protection standard -y stores
    // <group>.api_token as a standard secret and <group>.host as a public pair; kv import .env --format env --secret
    // '<group>.DB_*' -y stores <group>.db_password as a secret at the project's default secret level (kv policy) and
    // <group>.port as a public pair (the .env names come back lower-cased, "__" as '.'); no secret value is printed.
    [Test]
    [TestCase(427)]
    [UserRole(Role.Secrets)]
    [Covers("story kv import", "--format", "--secret", "--protection", "--format=flat", "--format=env", "--protection=standard")]
    public async Task KvImport_FlatEnvSecrets()
    {
        var group = CliData.KeyValueKey();
        var token = $"{group}.api_token";
        var host = $"{group}.host";
        var password = $"{group}.db_password";
        var port = $"{group}.port";
        var tokenValue = CliData.SecretValue();
        var passwordValue = CliData.SecretValue();
        AddSecret(tokenValue);
        AddSecret(passwordValue);
        var hostValue = CliData.Value();
        var portValue = CliData.Value();

        await WriteFileAsync("a.flat.json", new JsonObject { [token] = tokenValue, [host] = hostValue }.ToJsonString());
        (await Sform(new SformRunOptions { Secrets = [tokenValue] }, InProject("story", "kv", "import", "a.flat.json", "--format", "flat", "--secret", "*_TOKEN", "--protection", "standard", "-y")))
            .ShouldSucceed()
            .ShouldContain("Imported: 2 create.")
            .ShouldNotContain(tokenValue);
        await AssertLevelAsync(token, "Standard");
        await AssertLevelAsync(host, "Public");

        var defaultLevel = (await Sform(InProject("story", "kv", "policy"))).ShouldSucceed().JsonNode()["DefaultSecretProtection"]?.GetValue<string>();
        await Assert.That(defaultLevel).IsNotNull();
        await WriteFileAsync(".env", $"{KeyValuesSupportB.EnvName(password)}={passwordValue}\n{KeyValuesSupportB.EnvName(port)}={portValue}\n");
        (await Sform(new SformRunOptions { Secrets = [passwordValue] }, InProject("story", "kv", "import", ".env", "--format", "env", "--secret", $"{group}.DB_*", "-y")))
            .ShouldSucceed()
            .ShouldContain("Imported: 2 create.")
            .ShouldNotContain(passwordValue);
        await AssertLevelAsync(password, defaultLevel!);
        await AssertLevelAsync(port, "Public");

        var revealed = await Sform(new SformRunOptions { Secrets = [passwordValue] }, InProject("story", "kv", "get", password, "--reveal", "--value-only"));
        revealed.ShouldSucceed().ShouldHaveLine(passwordValue);
    }

    // KV-TRN-07: --mode replace --prefix deletes the other active keys under the prefix.
    // In a disposable project, <group>.app.alpha, <group>.app.beta and <group>.other.gamma: kv import kv.json --mode
    // replace --prefix <group>.app. -y with a new value of app.alpha and a new app.delta updates alpha, creates delta and
    // soft-deletes app.beta, which the file does not list, and keeps other.gamma; then kv import kv.json --mode merge
    // --prefix <group>.app. -y with a newer app.alpha only updates it and deletes nothing (delta and gamma stay).
    [Test]
    [TestCase(429)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv import", "--mode", "--prefix", "--mode=replace", "--mode=merge")]
    public async Task KvImport_ReplaceUnderPrefix()
    {
        await CreateDisposableProjectAsync();
        var group = CliData.KeyValueKey();
        var alpha = $"{group}.app.alpha";
        var beta = $"{group}.app.beta";
        var gamma = $"{group}.other.gamma";
        var delta = $"{group}.app.delta";
        var gammaValue = CliData.Value();
        (await Sform(InProject("story", "kv", "set", alpha, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", beta, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", gamma, gammaValue, "--protection", "public"))).ShouldSucceed();

        var alphaReplaced = CliData.Value();
        var deltaValue = CliData.Value();
        await WriteFileAsync("kv.json", KeyValuesSupportB.Document((alpha, "Public", alphaReplaced), (delta, "Public", deltaValue)));
        (await Sform(InProject("story", "kv", "import", "kv.json", "--mode", "replace", "--prefix", $"{group}.app.", "-y")))
            .ShouldSucceed()
            .ShouldContain("(Replace)")
            .ShouldContain("Imported: 1 create, 1 update, 1 delete.");

        (await Sform(InProject("story", "kv", "get", beta))).ShouldExitWith(CliExit.WrongInput);
        await AssertValueAsync(alpha, alphaReplaced);
        await AssertValueAsync(delta, deltaValue);
        await AssertValueAsync(gamma, gammaValue);

        var alphaMerged = CliData.Value();
        await WriteFileAsync("kv.json", KeyValuesSupportB.Document((alpha, "Public", alphaMerged)));
        (await Sform(InProject("story", "kv", "import", "kv.json", "--mode", "merge", "--prefix", $"{group}.app.", "-y")))
            .ShouldSucceed()
            .ShouldContain("(Merge)")
            .ShouldContain("Imported: 1 update.");
        await AssertValueAsync(alpha, alphaMerged);
        await AssertValueAsync(delta, deltaValue);
        await AssertValueAsync(gamma, gammaValue);
    }

    // KV-TRN-08: --dry-run shows the plan and writes nothing; a terminal asks before writing.
    // As an Editor, kv import kv.json --dry-run prints the plan ("Plan: 1 create.") and writes nothing (kv get is not
    // found); kv import kv.json in a terminal asks "Apply the plan?": No prints "Aborted, nothing was written." and ends
    // with 40, Yes writes the pair; then kv import kv.json, whose plan has nothing to change, prints "Nothing to change."
    // and ends with 42 without asking.
    [Test]
    [TestCase(431)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story kv import", "--dry-run")]
    public async Task KvImport_DryRunAndConfirm()
    {
        var key = CliData.KeyValueKey();
        var value = CliData.Value();
        await WriteFileAsync("kv.json", KeyValuesSupportB.Document((key, "Public", value)));

        (await Sform(InProject("story", "kv", "import", "kv.json", "--dry-run")))
            .ShouldSucceed()
            .ShouldContain("Plan: 1 create.")
            .ShouldNotContain("Imported:");
        (await Sform(InProject("story", "kv", "get", key))).ShouldExitWith(CliExit.WrongInput);

        await using (var declined = await StartSformInTerminal(InProject("story", "kv", "import", "kv.json")))
        {
            await declined.ExpectAsync("Apply the plan?");
            await declined.SendAsync("n");
            await declined.SendKeyAsync(TerminalKey.Enter);
            (await declined.WaitForExitAsync())
                .ShouldExitWith(CliExit.Aborted)
                .ShouldContain("Aborted, nothing was written.");
        }

        (await Sform(InProject("story", "kv", "get", key))).ShouldExitWith(CliExit.WrongInput);

        await using (var confirmed = await StartSformInTerminal(InProject("story", "kv", "import", "kv.json")))
        {
            await confirmed.ExpectAsync("Apply the plan?");
            await confirmed.SendAsync("y");
            await confirmed.SendKeyAsync(TerminalKey.Enter);
            (await confirmed.WaitForExitAsync())
                .ShouldSucceed()
                .ShouldContain("Imported: 1 create.");
        }

        await AssertValueAsync(key, value);

        (await Sform(InProject("story", "kv", "import", "kv.json")))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain("Nothing to change.")
            .ShouldNotContain("Imported:");
    }

    private static async Task AssertDocumentAsync(JsonNode? document, IReadOnlyDictionary<string, string> pairs)
    {
        await Assert.That(document?["Format"]?.GetValue<string>()).IsEqualTo(KeyValuesSupportB.DocumentFormat);
        var items = KeyValuesSupportB.ItemsOf(document);
        await Assert.That(items.Keys).IsEquivalentTo(pairs.Keys);
        foreach (var (key, value) in pairs)
        {
            await Assert.That(items[key]["Value"]?.GetValue<string>()).IsEqualTo(value);
            await Assert.That(items[key]["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        }
    }

    private static async Task AssertEnvAsync(IReadOnlyDictionary<string, string> entries, IReadOnlyDictionary<string, string> pairs)
    {
        await Assert.That(entries.Keys).IsEquivalentTo(pairs.Keys.Select(KeyValuesSupportB.EnvName));
        foreach (var (key, value) in pairs)
        {
            await Assert.That(entries[KeyValuesSupportB.EnvName(key)]).IsEqualTo(value);
        }
    }

    private async Task AssertLevelAsync(string key, string level)
    {
        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo(level);
    }

    private async Task AssertValueAsync(string key, string value)
    {
        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
    }
}
