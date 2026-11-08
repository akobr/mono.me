using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Get and Formats: sform story config reads a configuration as its model, resolved, or as content in a format.
[Area(Area.Configurations)]
public sealed class ConfigGetTests : CliTest
{
    // CFG-GET-01: story config (and configuration) returns the configuration model.
    // An Editor creates <rst> with a configuration (story set, config set -x); as a Reader, story config <rst> prints the
    // model with the content and its version, and story configuration <rst> (the alias) the same content.
    [Test]
    [TestCase(368)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story config", "arg0")]
    [Covers("story configuration", "arg0")]
    public async Task Config_Get_ReturnsModel()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", $"probe={probe}", "-x", "count=3"))).ShouldSucceed();

        var model = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(model["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(model["Content"]?["count"]?.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(model["Version"]?.GetValue<int>() ?? 0).IsGreaterThanOrEqualTo(1);

        var alias = (await Sform(InProject("story", "configuration", key))).ShouldSucceed().JsonNode();
        await Assert.That(alias["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(alias["Version"]?.GetValue<int>()).IsEqualTo(model["Version"]!.GetValue<int>());
    }

    // CFG-GET-02: -r returns the resolved configuration with bindings and inheritance applied.
    // An Editor creates a public pair, a parent configuration on <rst> and one on its unit <rst>.<unt> that binds the
    // pair (@<key>); as a Reader, story config <unt> -r (and --resolved) has the parent's value merged in and the pair's
    // value in place of the binding, while the plain read keeps the binding expression.
    [Test]
    [TestCase(369)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story config", "-r", "--resolved")]
    public async Task Config_Resolved_AppliesBindings()
    {
        var editor = await ArrangeAsEditorAsync();
        var parent = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(parent);
        var pair = CliData.KeyValueKey();
        var pairValue = CliData.Value();
        var inherited = CliData.Value();
        var own = CliData.Value();
        (await editor.Sform(InProject("story", "set", parent))).ShouldSucceed();
        (await editor.Sform(InProject("story", "set", unit))).ShouldSucceed();
        (await editor.Sform(InProject("story", "kv", "set", pair, pairValue, "--protection", "public"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", parent, "-x", $"inherited={inherited}"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", unit, "-x", $"own={own}", "-x", $"bound=@{pair}"))).ShouldSucceed();

        foreach (var option in new[] { "-r", "--resolved" })
        {
            var resolved = (await Sform(InProject("story", "config", unit, option))).ShouldSucceed().JsonNode()["Content"];
            await Assert.That(resolved?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
            await Assert.That(resolved?["own"]?.GetValue<string>()).IsEqualTo(own);
            await Assert.That(resolved?["bound"]?.GetValue<string>()).IsEqualTo(pairValue);
        }

        var effective = (await Sform(InProject("story", "config", unit))).ShouldSucceed().JsonNode()["Content"];
        await Assert.That(effective?["bound"]?.GetValue<string>()).IsEqualTo($"@{pair}");
        await Assert.That(effective?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
    }

    // CFG-GET-03: -f prints the content as flat, properties, env and yaml, and -f json the model.
    // An Editor stores {"db":{"host":<v>,"port":5432},"enabled":true} on <rst> (config set -i) in a generated view of the
    // project, where no template of another test adds values; as a Reader, config <rst> -f flat, --format properties,
    // -f env, -f yaml and -f json, each with --content-only, print exactly the same three values in their format
    // (configuration-formats.md: flat keys db:host as strings, db.host=…, DB__HOST=…, YAML with the string quoted and the
    // number and boolean plain, the content JSON). Without --content-only, -f yaml prints the YAML with the line-number
    // gutter, and -f json the model (Content and Version), as without -f.
    [Test]
    [TestCase(370)]
    [UserRole(Role.Reader)]
    [Covers("story config", "-f", "--format", "--format=flat", "--format=properties", "--format=env", "--format=yaml", "--format=json")]
    public async Task Config_Formats_RenderContent()
    {
        var editor = await ArrangeAsEditorAsync();
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var host = CliData.Value();
        var file = await WriteFileAsync("c.json", $$"""{ "db": { "host": "{{host}}", "port": 5432 }, "enabled": true }""");
        (await editor.Sform(At("story", "set", key))).ShouldSucceed();
        (await editor.Sform(At("story", "config", "set", key, "-i", file))).ShouldSucceed();
        string[] yaml = ["db:", $"  host: \"{host}\"", "  port: 5432", "enabled: true"];

        var flat = JsonNode.Parse((await Sform(At("story", "config", key, "-f", "flat", "--content-only"))).ShouldSucceed().PlainStdOut)!.AsObject();
        await Assert.That(flat.Count).IsEqualTo(3);
        await Assert.That(flat["db:host"]?.GetValue<string>()).IsEqualTo(host);
        await Assert.That(flat["db:port"]?.GetValue<string>()).IsEqualTo("5432");
        await Assert.That(flat["enabled"]?.GetValue<string>()).IsEqualTo("true");

        var properties = (await Sform(At("story", "config", key, "--format", "properties", "--content-only"))).ShouldSucceed();
        await Assert.That(string.Join('\n', ConfigOutput.LinesOf(properties.PlainStdOut))).IsEqualTo($"db.host={host}\ndb.port=5432\nenabled=true");

        var env = (await Sform(At("story", "config", key, "-f", "env", "--content-only"))).ShouldSucceed();
        await Assert.That(string.Join('\n', ConfigOutput.LinesOf(env.PlainStdOut))).IsEqualTo($"DB__HOST={host}\nDB__PORT=5432\nENABLED=true");

        var yamlOnly = (await Sform(At("story", "config", key, "-f", "yaml", "--content-only"))).ShouldSucceed();
        await Assert.That(string.Join('\n', ConfigOutput.LinesOf(yamlOnly.PlainStdOut))).IsEqualTo(string.Join('\n', yaml));

        var json = JsonNode.Parse((await Sform(At("story", "config", key, "-f", "json", "--content-only"))).ShouldSucceed().PlainStdOut)!;
        await Assert.That(json["Content"]).IsNull();
        await Assert.That(json["db"]?["host"]?.GetValue<string>()).IsEqualTo(host);
        await Assert.That(json["db"]?["port"]?.GetValue<long>()).IsEqualTo(5432L);
        await Assert.That(json["enabled"]?.GetValue<bool>()).IsTrue();

        var numbered = (await Sform(At("story", "config", key, "-f", "yaml"))).ShouldSucceed();
        await Assert.That(string.Join('\n', ConfigOutput.NumberedLines(numbered))).IsEqualTo(string.Join('\n', yaml));

        var model = (await Sform(At("story", "config", key, "-f", "json"))).ShouldSucceed().JsonNode();
        await Assert.That(model["AnnotationKey"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(model["Version"]?.GetValue<long>() ?? 0).IsGreaterThanOrEqualTo(1L);
        await Assert.That(model["Content"]?["db"]?["host"]?.GetValue<string>()).IsEqualTo(host);

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-GET-04: --content-only prints the content alone, and messages go to stderr.
    // An Editor stores probe and count on <rst> in a generated view of the project; as a Reader, config <rst>
    // --content-only prints the content JSON and nothing else (the whole standard output is the JSON object, without the
    // model's Content or Version and without the gutter), and -f yaml --content-only the YAML alone. A message of such a
    // run goes to the standard error: config not-a-key --content-only ends with 2, the standard output empty and "Please
    // specify a valid annotation key; …" on the standard error.
    [Test]
    [TestCase(371)]
    [UserRole(Role.Reader)]
    [Covers("story config", "--content-only")]
    public async Task Config_ContentOnly_PipesContent()
    {
        var editor = await ArrangeAsEditorAsync();
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        (await editor.Sform(At("story", "set", key))).ShouldSucceed();
        (await editor.Sform(At("story", "config", "set", key, "-x", $"probe={probe}", "-x", "count=3"))).ShouldSucceed();

        var content = (await Sform(At("story", "config", key, "--content-only"))).ShouldSucceed();
        await Assert.That(content.PlainStdOut.TrimStart()).StartsWith("{");
        await Assert.That(content.PlainStdOut).DoesNotContain("│");
        var json = JsonNode.Parse(content.PlainStdOut)!;
        await Assert.That(json["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(json["count"]?.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(json["Content"]).IsNull();
        await Assert.That(json["Version"]).IsNull();

        var yaml = (await Sform(At("story", "config", key, "-f", "yaml", "--content-only"))).ShouldSucceed();
        await Assert.That(string.Join('\n', ConfigOutput.LinesOf(yaml.PlainStdOut).Order(StringComparer.Ordinal)))
            .IsEqualTo(string.Join('\n', new[] { "count: 3", $"probe: \"{probe}\"" }.Order(StringComparer.Ordinal)));

        var invalid = (await Sform(At("story", "config", "not-a-key", "--content-only")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Please specify a valid annotation key; 'sform assist key' explains them.", SformStream.StdErr);
        await Assert.That(invalid.PlainStdOut.Trim()).IsEmpty();

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-GET-05: -e saves the model, or the formatted content with -f, into a file.
    // As a Reader, config <rst> -e m.json prints the model and writes it to m.json (AnnotationKey and Content); config
    // <rst> --export c.env -f env writes the env content (PROBE=…, COUNT=3) to c.env and leaves the standard output empty.
    [Test]
    [TestCase(372)]
    [UserRole(Role.Reader)]
    [Covers("story config", "-e", "--export")]
    public async Task Config_Export_WritesFile()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", $"probe={probe}", "-x", "count=3"))).ShouldSucceed();

        var printed = (await Sform(InProject("story", "config", key, "-e", "m.json"))).ShouldSucceed().JsonNode();
        await Assert.That(printed["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        var saved = JsonNode.Parse(await ReadFileAsync("m.json"))!;
        await Assert.That(saved["AnnotationKey"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(saved["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(saved["Content"]?["count"]?.GetValue<long>()).IsEqualTo(3L);

        var export = (await Sform(InProject("story", "config", key, "--export", "c.env", "-f", "env"))).ShouldSucceed();
        await Assert.That(export.PlainStdOut.Trim()).IsEmpty();
        var lines = ConfigOutput.LinesOf(await ReadFileAsync("c.env"));
        await Assert.That(lines).Contains($"PROBE={probe}");
        await Assert.That(lines).Contains("COUNT=3");
    }

    // CFG-GET-06: --version returns the stored content of an old version.
    // An Editor writes three versions of <rst> (a=1, a=2, a=3); as a Reader, config <rst> --version <first> returns the
    // model of the first version with a=1, while the current read has a=3.
    [Test]
    [TestCase(373)]
    [UserRole(Role.Reader)]
    [Covers("story config", "--version")]
    public async Task Config_Version_ReturnsOld()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        var first = (await editor.Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", "a=2"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", "a=3"))).ShouldSucceed();

        var old = (await Sform(InProject("story", "config", key, "--version", first.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ShouldSucceed().JsonNode();
        await Assert.That(old["Version"]?.GetValue<long>()).IsEqualTo(first);
        await Assert.That(old["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);

        var current = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(current["Content"]?["a"]?.GetValue<long>()).IsEqualTo(3L);
    }

    // CFG-GET-07: --env-prefix prefixes every env name, and an invalid prefix fails with exit 2.
    // As a Reader, config <rst> -f env --env-prefix APP_ prints the env lines (with the gutter) and every name starts
    // with APP_ (APP_PROBE=…, APP_COUNT=3); --env-prefix 1X ends with 2 and "--env-prefix must start with an ASCII letter
    // …" before any call.
    [Test]
    [TestCase(375)]
    [UserRole(Role.Reader)]
    [Covers("story config", "--env-prefix")]
    public async Task Config_EnvPrefix_Prefixes()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", $"probe={probe}", "-x", "count=3"))).ShouldSucceed();

        var lines = ConfigOutput.NumberedLines((await Sform(InProject("story", "config", key, "-f", "env", "--env-prefix", "APP_"))).ShouldSucceed());
        await Assert.That(lines.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(lines.All(line => line.StartsWith("APP_", StringComparison.Ordinal))).IsTrue().Because(string.Join(" | ", lines));
        await Assert.That(lines).Contains($"APP_PROBE={probe}");
        await Assert.That(lines).Contains("APP_COUNT=3");

        (await Sform(InProject("story", "config", key, "-f", "env", "--env-prefix", "1X")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_.");
    }

    // CFG-GET-08: A secret binding stays unresolved for a Contributor, with a warning, and resolves for
    // ContributorWithSecrets.
    // In the disposable project the Owner stores a secret <s> (kv set <s> <secret> --secret) and a configuration of <rst>
    // with "password": "@<s>", and grants a second pool user Contributor there. config <rst> -r as the Contributor ends
    // with 0, has "password": "@<s>" as stored and no secret, and its standard error has "Warning: these values are
    // binding expressions that were not resolved: …" naming password and "The caller may lack Secrets.Read."; the
    // Owner's read has the secret's value and no warning.
    [Test]
    [TestCase(377)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story config", "-r")]
    public async Task Config_ResolvedSecretAsContributor_LeftUnresolved()
    {
        await CreateDisposableProjectAsync();
        var key = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", pair, secret, "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"password=@{pair}"))).ShouldSucceed();
        var contributor = await AsGrantedUserAsync(PlatformRoles.Contributor);

        var withheld = (await contributor.Sform(InProject("story", "config", key, "-r")))
            .ShouldSucceed()
            .ShouldContain("Warning: these values are binding expressions that were not resolved:", SformStream.StdErr)
            .ShouldContain("password", SformStream.StdErr)
            .ShouldContain("The caller may lack Secrets.Read.", SformStream.StdErr)
            .ShouldNotContain(secret)
            .ShouldNotContain(secret, SformStream.StdErr);
        await Assert.That(withheld.JsonNode()["Content"]?["password"]?.GetValue<string>()).IsEqualTo($"@{pair}");

        var resolved = (await Sform(InProject("story", "config", key, "-r")))
            .ShouldSucceed()
            .ShouldNotContain("Warning:", SformStream.StdErr);
        await Assert.That(resolved.JsonNode()["Content"]?["password"]?.GetValue<string>()).IsEqualTo(secret);
    }
}
