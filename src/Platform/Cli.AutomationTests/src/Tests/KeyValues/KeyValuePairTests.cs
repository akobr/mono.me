using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Pairs: sform story kv set and get of public pairs, the value sources and the versions.
[Area(Area.KeyValues)]
public sealed class KeyValuePairTests : CliTest
{
    // KV-PAR-01: kv set with --protection public and kv get round-trip a public pair.
    // As an Editor, story kv set <key> <value> --protection public says the pair is at version 1 (Public), and
    // story kv get <key> returns it with the value, the level Public and version 1.
    [Test]
    [TestCase(349)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "arg0", "arg1", "--protection", "--protection=public")]
    [Covers("story kv get", "arg0")]
    public async Task Kv_SetGetPublic_RoundTrip()
    {
        var key = CliData.KeyValueKey();
        var value = CliData.Value();

        (await Sform(InProject("story", "kv", "set", key, value, "--protection", "public")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Public).");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Key"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(1);
    }

    // KV-PAR-02: kv create is an alias, and an update without --protection keeps the level.
    // As a ContributorWithSecrets, story kv set <key> <v1> --protection standard stores a Standard secret at version 1;
    // story kv create <key> <v2> (the alias of set, without a level) writes version 2 at the stored level: "'<key>' is at
    // version 2 (Standard).", and kv get shows Standard at version 2.
    [Test]
    [TestCase(351)]
    [UserRole(Role.Secrets)]
    [Covers("story kv create")]
    public async Task Kv_UpdateKeepsLevel()
    {
        var key = CliData.KeyValueKey();
        var first = CliData.SecretValue();
        var second = CliData.SecretValue();
        AddSecret(first);
        AddSecret(second);

        (await Sform(InProject("story", "kv", "set", key, first, "--protection", "standard")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Standard).");

        (await Sform(InProject("story", "kv", "create", key, second)))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 2 (Standard).")
            .ShouldNotContain("New key, stored as Public");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(2);
    }

    // KV-PAR-03: A new key without --protection or --secret is stored as public.
    // As an Editor, story kv set <new key> <value> says "New key, stored as Public; use --secret or --protection for a
    // secret." and "'<new key>' is at version 1 (Public).", and exits 0.
    [Test]
    [TestCase(353)]
    [UserRole(Role.Editor)]
    [Covers("story kv set")]
    public async Task Kv_NewWithoutLevel_StoredPublic()
    {
        var key = CliData.KeyValueKey();

        (await Sform(InProject("story", "kv", "set", key, CliData.Value())))
            .ShouldSucceed()
            .ShouldContain("New key, stored as Public; use --secret or --protection for a secret.")
            .ShouldContain($"'{key}' is at version 1 (Public).");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Public");
    }

    // KV-PAR-04: --stdin and -i read the value from standard input and from a file.
    // As an Editor, story kv set <key> --protection public --stdin stores the value written to the standard input; then
    // story kv set <key> -i v.txt and story kv set <key> --import v2.txt store the content of each file (versions 2 and
    // 3). kv get returns each value, and none of them is on a command line.
    [Test]
    [TestCase(355)]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "--stdin", "-i", "--import")]
    public async Task Kv_ValueSources_StdinAndFile()
    {
        var key = CliData.KeyValueKey();
        var fromStdin = CliData.Value();
        var fromFile = CliData.Value();
        var fromImport = CliData.Value();
        await WriteFileAsync("v.txt", fromFile);
        await WriteFileAsync("v2.txt", fromImport);

        var stdin = (await Sform(new SformRunOptions { Input = fromStdin + "\n" }, InProject("story", "kv", "set", key, "--protection", "public", "--stdin")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Public).");
        await Assert.That(stdin.CommandLine).DoesNotContain(fromStdin);
        await Assert.That(await ValueOfAsync(key)).IsEqualTo(fromStdin);

        var file = (await Sform(InProject("story", "kv", "set", key, "-i", "v.txt")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 2 (Public).");
        await Assert.That(file.CommandLine).DoesNotContain(fromFile);
        await Assert.That(await ValueOfAsync(key)).IsEqualTo(fromFile);

        var import = (await Sform(InProject("story", "kv", "set", key, "--import", "v2.txt")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 3 (Public).");
        await Assert.That(import.CommandLine).DoesNotContain(fromImport);
        await Assert.That(await ValueOfAsync(key)).IsEqualTo(fromImport);
    }

    // KV-PAR-05: --content-type stores the content type.
    // As an Editor, story kv set <key> '{"a":1}' --protection public --content-type application/json; kv get shows the
    // content type application/json and the value.
    [Test]
    [TestCase(356)]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "--content-type")]
    public async Task Kv_ContentType_Stored()
    {
        var key = CliData.KeyValueKey();
        const string json = """{"a":1}""";

        (await Sform(InProject("story", "kv", "set", key, json, "--protection", "public", "--content-type", "application/json"))).ShouldSucceed();

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["ContentType"]?.GetValue<string>()).IsEqualTo("application/json");
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(json);
    }

    // KV-PAR-06: --expected-version writes only at that version, and 0 only when the key does not exist.
    // As an Editor, story kv set <new> v --protection public --expected-version 0 creates the key; the same again fails
    // with exit 2 (the key exists); story kv set <new> w --expected-version 1 writes version 2; the same again fails with
    // exit 2 (the key is at version 2). kv get shows w at version 2.
    [Test]
    [TestCase(361)]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "--expected-version")]
    public async Task Kv_ExpectedVersion_Concurrency()
    {
        var key = CliData.KeyValueKey();
        var first = CliData.Value();
        var second = CliData.Value();

        (await Sform(InProject("story", "kv", "set", key, first, "--protection", "public", "--expected-version", "0")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Public).");
        (await Sform(InProject("story", "kv", "set", key, first, "--protection", "public", "--expected-version", "0")))
            .ShouldExitWith(CliExit.WrongInput);

        (await Sform(InProject("story", "kv", "set", key, second, "--expected-version", "1")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 2 (Public).");
        (await Sform(InProject("story", "kv", "set", key, second, "--expected-version", "1")))
            .ShouldExitWith(CliExit.WrongInput);

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(second);
        await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(2);
    }

    // KV-PAR-07: kv get --value-only prints the value alone, and -e saves the pair as JSON.
    // An Editor sets a public pair; as a Reader, story kv get <key> --value-only prints only the value on the standard
    // output, and story kv get <key> -e p.json and --export p2.json save the pair (key, value, level) into the files.
    [Test]
    [TestCase(362)]
    [UserRole(Role.Reader)]
    [Covers("story kv get", "--value-only", "-e", "--export")]
    public async Task Kv_GetValueOnlyAndExport()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.KeyValueKey();
        var value = CliData.Value();
        (await editor.Sform(InProject("story", "kv", "set", key, value, "--protection", "public"))).ShouldSucceed();

        var valueOnly = (await Sform(InProject("story", "kv", "get", key, "--value-only"))).ShouldSucceed();
        await Assert.That(valueOnly.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray()).IsEquivalentTo(new[] { value });

        foreach (var (option, file) in new[] { ("-e", "p.json"), ("--export", "p2.json") })
        {
            (await Sform(InProject("story", "kv", "get", key, option, file))).ShouldSucceed();
            var exported = JsonNode.Parse(await ReadFileAsync(file))!;
            await Assert.That(exported["Key"]?.GetValue<string>()).IsEqualTo(key);
            await Assert.That(exported["Value"]?.GetValue<string>()).IsEqualTo(value);
            await Assert.That(exported["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        }
    }

    // KV-PAR-08: kv get --version returns an old version, and --deleted finds a soft-deleted key.
    // An Editor sets a public pair twice; as a Reader, story kv get <key> --version 1 returns the first value at version 1.
    // The Editor deletes the key; story kv get <key> then ends with exit 2 (not found), and story kv get <key> --deleted
    // returns the deleted pair with its expiry.
    [Test]
    [TestCase(364)]
    [UserRole(Role.Reader)]
    [Covers("story kv get", "--version", "--deleted")]
    public async Task Kv_GetVersionAndDeleted()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.KeyValueKey();
        var first = CliData.Value();
        var second = CliData.Value();
        (await editor.Sform(InProject("story", "kv", "set", key, first, "--protection", "public"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "kv", "set", key, second))).ShouldSucceed();

        var old = (await Sform(InProject("story", "kv", "get", key, "--version", "1"))).ShouldSucceed().JsonNode();
        await Assert.That(old["Value"]?.GetValue<string>()).IsEqualTo(first);
        await Assert.That(old["Version"]?.GetValue<int>()).IsEqualTo(1);

        (await editor.Sform(InProject("story", "kv", "delete", key))).ShouldSucceed();

        (await Sform(InProject("story", "kv", "get", key))).ShouldExitWith(CliExit.WrongInput);

        var deleted = (await Sform(InProject("story", "kv", "get", key, "--deleted"))).ShouldSucceed().JsonNode();
        await Assert.That(deleted["Key"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(deleted["IsDeleted"]?.GetValue<bool>()).IsTrue();
        await Assert.That(deleted["ExpiresAt"]).IsNotNull();
    }

    // KV-PAR-09: --reference stores a Key Vault reference as a public pair, and a binding inserts it as text.
    // As an Editor, story kv set <key> kv://<vault>/<name> --reference says "'<key>' is at version 1 (Public)."; kv get
    // shows the level Public and the content type application/vnd.2s.secret-reference; a configuration of <rst> with
    // secret=@<key> resolves (config <rst> -r) to the reference's text, as the platform never resolves a reference. Then
    // story kv set <key2> kv://<vault>/<name> --reference --secret ends with exit 2 and "A reference is a public pair; the
    // platform never resolves it. Drop --protection or --secret."
    [Test]
    [TestCase(365)]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "--reference")]
    public async Task Kv_Reference_StoredAsText()
    {
        var key = CliData.KeyValueKey();
        var secondKey = CliData.KeyValueKey();
        var reference = $"kv://cli-{CliData.Data.Hex(8)}/{CliData.Slug("ref")}";

        (await Sform(InProject("story", "kv", "set", key, reference, "--reference")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Public).");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        await Assert.That(pair["ContentType"]?.GetValue<string>()).IsEqualTo("application/vnd.2s.secret-reference");
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(reference);

        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", rst, "-x", $"secret=@{key}"))).ShouldSucceed();
        var resolved = (await Sform(InProject("story", "config", rst, "-r"))).ShouldSucceed().JsonNode()["Content"];
        await Assert.That(resolved?["secret"]?.GetValue<string>()).IsEqualTo(reference);

        (await Sform(InProject("story", "kv", "set", secondKey, reference, "--reference", "--secret")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("A reference is a public pair; the platform never resolves it. Drop --protection or --secret.");
    }

    // KV-PAR-10: A Reader's kv set is refused, and a machine needs Configuration.Write.
    // As a Reader, story kv set <key> v --protection public ends with exit 44; a machine created with -r (an API key with
    // AnnotationRead and ConfigurationRead) by an Editor of Target:Project is refused the same set with exit 44.
    [Test]
    [TestCase(366)]
    [UserRole(Role.Reader)]
    [Covers("story kv set")]
    public async Task Kv_WriteRights()
    {
        var key = CliData.KeyValueKey();

        (await Sform(InProject("story", "kv", "set", key, CliData.Value(), "--protection", "public")))
            .ShouldExitWith(CliExit.Unauthorized);

        var creator = await AsAnotherUserAsync(EditingUser);
        var reader = await CreateMachineAsync(new MachineCreation { As = creator, ProjectKey = Target.ProjectKey, View = Target.View }, "-k", "ApiKey", "-r");
        (await Sform(AsMachine(reader.ApiKeyCredentials), "story", "kv", "set", key, CliData.Value(), "--protection", "public"))
            .ShouldExitWith(CliExit.Unauthorized);
    }

    // The value kv get returns for a public pair of the acting project.
    private async Task<string?> ValueOfAsync(string key)
    {
        return (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode()["Value"]?.GetValue<string>();
    }
}
