using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// GitHub Actions and Masking: story config export, the resolved configuration handed to a CI job (--github-env,
// --prefix, --mask, the --allow-* options and the file of -e with -f), and story config, which no longer exports.
[Area(Area.Configurations)]
public sealed class ConfigGitHubTests : CliTest
{
    private const string AddMask = "::add-mask::";

    // CFG-GHA-01: config export --github-env with --prefix writes the resolved values into GITHUB_ENV and masks bound
    // values.
    // As a ContributorWithSecrets, a secret pair and a configuration of <rst> with a plain value and a binding to the
    // secret; story config export <rst> --github-env --prefix APP_ with GITHUB_ENV naming an empty file writes APP_PLAIN
    // and APP_TOKEN there in the heredoc form (NAME<<ghadelimiter_…), prints ::add-mask:: for the bound secret only and
    // nothing else on the standard output (no content: neither value is there but in that mask), and says "Exported …
    // variable(s) to GITHUB_ENV: …" naming both on the standard error.
    [Test]
    [TestCase(436)]
    [Elementary]
    [UserRole(Role.Secrets)]
    [Covers("story config export", "arg0", "--github-env", "--prefix")]
    public async Task GitHubEnv_Prefixed_ExportsAndMasks()
    {
        var key = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        var plain = CliData.Value();
        AddSecret(secret);
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", pair, secret, "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}", "-x", $"token=@{pair}"))).ShouldSucceed();
        var githubEnv = await WriteFileAsync("github_env", string.Empty);

        var export = await Sform(
            new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } },
            InProject("story", "config", "export", key, "--github-env", "--prefix", "APP_"));

        export.ShouldSucceed()
            .ShouldContain($"{AddMask}{secret}")
            .ShouldNotContain($"{AddMask}{plain}")
            .ShouldNotContain(plain)
            .ShouldContain("variable(s) to GITHUB_ENV:", SformStream.StdErr)
            .ShouldContain("APP_PLAIN", SformStream.StdErr)
            .ShouldContain("APP_TOKEN", SformStream.StdErr);
        await Assert.That(OnlyMasks(export)).IsTrue().Because(export.PlainStdOut);
        await Assert.That(export.PlainStdOut.Replace($"{AddMask}{secret}", string.Empty, StringComparison.Ordinal)).DoesNotContain(secret);

        var written = await ReadFileAsync(githubEnv);
        await Assert.That(written).Matches(@"(?m)^APP_PLAIN<<ghadelimiter_[0-9a-f]{32}$");
        await Assert.That(written).Matches(@"(?m)^APP_TOKEN<<ghadelimiter_[0-9a-f]{32}$");
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "APP_PLAIN")).IsEqualTo(plain);
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "APP_TOKEN")).IsEqualTo(secret);
    }

    // CFG-GHA-02: --github-env without --prefix fails, and --allow-unprefixed allows it.
    // As a ContributorWithSecrets, with plain=<v> stored on <rst> and GITHUB_ENV naming an empty file: config export <rst>
    // --github-env ends with 2 before any call, with "--github-env needs --prefix, for example APP_, or
    // --allow-unprefixed: …" on the standard error, nothing on the standard output, and nothing written; with
    // --allow-unprefixed it exports the names without a prefix (PLAIN<<ghadelimiter_…, the value) and says "Exported …
    // to GITHUB_ENV: … PLAIN …" on the standard error.
    [Test]
    [TestCase(437)]
    [UserRole(Role.Secrets)]
    [Covers("story config export", "--allow-unprefixed")]
    public async Task GitHubEnv_Unprefixed_NeedsAllow()
    {
        var key = CliData.ResponsibilityKey();
        var plain = CliData.Value();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}"))).ShouldSucceed();
        var githubEnv = await WriteFileAsync("github_env", string.Empty);
        var options = new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } };

        var refused = (await Sform(options, InProject("story", "config", "export", key, "--github-env")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("--github-env needs --prefix, for example APP_, or --allow-unprefixed:", SformStream.StdErr);
        await Assert.That(refused.PlainStdOut.Trim()).IsEmpty();
        await Assert.That(await ReadFileAsync(githubEnv)).IsEmpty();

        (await Sform(options, InProject("story", "config", "export", key, "--github-env", "--allow-unprefixed")))
            .ShouldSucceed()
            .ShouldContain("to GITHUB_ENV:", SformStream.StdErr)
            .ShouldContain("PLAIN", SformStream.StdErr);
        var written = await ReadFileAsync(githubEnv);
        await Assert.That(ConfigOutput.GitHubEnvNames(written)).Contains("PLAIN");
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "PLAIN")).IsEqualTo(plain);
    }

    // CFG-GHA-03: --mask bound and --mask all print ::add-mask:: for bound values or every value.
    // As a ContributorWithSecrets, with a secret pair and a configuration of <rst> with plain=<v>, count=12345,
    // flag=true and token=@<secret pair>: config export <rst> --mask bound prints ::add-mask:: for the bound secret only
    // ("Masked … value(s) (bound)." on the standard error); --mask all prints it for every string and number (the plain
    // value, 12345 and the secret), never for the boolean ("Masked … value(s) (all)."). --mask alone exports nothing else:
    // the standard output has only the masks, and the standard error neither "GITHUB_ENV" nor "Wrote the configuration".
    [Test]
    [TestCase(438)]
    [UserRole(Role.Secrets)]
    [Covers("story config export", "--mask", "--mask=bound", "--mask=all")]
    public async Task Mask_BoundOrAll()
    {
        var key = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        var plain = CliData.Value();
        AddSecret(secret);
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", pair, secret, "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}", "-x", "count=12345", "-x", "flag=true", "-x", $"token=@{pair}"))).ShouldSucceed();

        var bound = (await Sform(InProject("story", "config", "export", key, "--mask", "bound")))
            .ShouldSucceed()
            .ShouldContain($"{AddMask}{secret}")
            .ShouldNotContain($"{AddMask}{plain}")
            .ShouldNotContain($"{AddMask}12345")
            .ShouldNotContain($"{AddMask}true")
            .ShouldContain("value(s) (bound).", SformStream.StdErr)
            .ShouldNotContain("GITHUB_ENV", SformStream.StdErr)
            .ShouldNotContain("Wrote the configuration", SformStream.StdErr);
        await Assert.That(OnlyMasks(bound)).IsTrue().Because(bound.PlainStdOut);

        var all = (await Sform(InProject("story", "config", "export", key, "--mask", "all")))
            .ShouldSucceed()
            .ShouldContain($"{AddMask}{secret}")
            .ShouldContain($"{AddMask}{plain}")
            .ShouldContain($"{AddMask}12345")
            .ShouldNotContain($"{AddMask}true")
            .ShouldContain("value(s) (all).", SformStream.StdErr);
        await Assert.That(OnlyMasks(all)).IsTrue().Because(all.PlainStdOut);
    }

    // CFG-GHA-04: An unresolved binding fails the export, and --allow-unresolved exports it as it is.
    // As a ContributorWithSecrets, with a configuration of <rst> with plain=<v> and missing=@<a key that does not exist>
    // (a missing key declines, so the resolved value stays the expression) and GITHUB_ENV naming an empty file: config
    // export <rst> --github-env --prefix A_ ends with 2, with "These values are binding expressions that were not
    // resolved: …" naming the binding (missing) and --allow-unresolved on the standard error, and writes nothing; with
    // --allow-unresolved it exports A_MISSING with the expression as it is and A_PLAIN, and notes "Binding expressions
    // that were not resolved, exported as they are: …" on the standard error.
    [Test]
    [TestCase(439)]
    [UserRole(Role.Secrets)]
    [Covers("story config export", "--allow-unresolved")]
    public async Task GitHubEnv_Unresolved_NeedsAllow()
    {
        var key = CliData.ResponsibilityKey();
        var missing = CliData.KeyValueKey();
        var plain = CliData.Value();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}", "-x", $"missing=@{missing}"))).ShouldSucceed();
        var githubEnv = await WriteFileAsync("github_env", string.Empty);
        var options = new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } };

        (await Sform(options, InProject("story", "config", "export", key, "--github-env", "--prefix", "A_")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("These values are binding expressions that were not resolved:", SformStream.StdErr)
            .ShouldContain("missing", SformStream.StdErr)
            .ShouldContain("--allow-unresolved", SformStream.StdErr);
        await Assert.That(await ReadFileAsync(githubEnv)).IsEmpty();

        (await Sform(options, InProject("story", "config", "export", key, "--github-env", "--prefix", "A_", "--allow-unresolved")))
            .ShouldSucceed()
            .ShouldContain("Binding expressions that were not resolved, exported as they are:", SformStream.StdErr)
            .ShouldContain("missing", SformStream.StdErr);
        var written = await ReadFileAsync(githubEnv);
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "A_MISSING")).IsEqualTo($"@{missing}");
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "A_PLAIN")).IsEqualTo(plain);
    }

    // CFG-GHA-05: story config refuses --github-env with exit -1, and config export without a target fails with exit 2.
    // As a Reader: config <rst> --github-env --env-prefix A_ ends with -1 (input parsing) and "Unrecognized option
    // '--github-env'" (story config only reads; the export moved to config export); config export <rst>, without
    // --github-env, --export or --mask, ends with 2 before any call, with "Nothing to export: add --github-env, --export
    // <file> or both, or --mask alone to print only the masks." on the standard error and nothing on the standard output.
    [Test]
    [TestCase(440)]
    [UserRole(Role.Reader)]
    [Covers("story config")]
    [Covers("story config export")]
    public async Task Export_FromConfigOrWithoutTarget_Fails()
    {
        var key = CliData.ResponsibilityKey();

        (await Sform(InProject("story", "config", key, "--github-env", "--env-prefix", "A_")))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("Unrecognized option '--github-env'");

        var refused = (await Sform(InProject("story", "config", "export", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Nothing to export: add --github-env, --export <file> or both, or --mask alone to print only the masks.", SformStream.StdErr);
        await Assert.That(refused.PlainStdOut.Trim()).IsEmpty();
    }

    // CFG-GHA-06: config export -e writes the resolved configuration to a file, in env by default or in the format of -f.
    // As a ContributorWithSecrets, in a generated view of the project (where no template of another test adds values): a
    // secret pair <s> and a configuration of <rst> {"db":{"host":<v>,"port":5432},"token":"@<s>"}. config export <rst>
    // -e app.env writes DB__HOST=<v>, DB__PORT=5432 and TOKEN=<secret> (env, the default); --export prefixed.env -f env
    // --prefix APP_ the same names with APP_; -e app.flat --format flat the flat JSON (db:host, db:port as a string,
    // token); -e app.properties -f properties the properties lines; -e app.yaml -f yaml the YAML (the strings quoted, the
    // number plain); -e app.json -f json the content JSON alone (no model): each with the secret resolved. Every run
    // prints only ::add-mask::<secret> on the standard output, and "Masked 1 value(s) (bound)." and "Wrote the
    // configuration to <file>." on the standard error.
    [Test]
    [TestCase(463)]
    [UserRole(Role.Secrets)]
    [Covers("story config export", "-e", "--export", "-f", "--format", "--format=env", "--format=flat", "--format=properties", "--format=yaml", "--format=json", "--prefix")]
    public async Task Export_File_WritesFormats()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        var host = CliData.Value();
        AddSecret(secret);
        (await Sform(At("story", "set", key))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret] }, At("story", "kv", "set", pair, secret, "--secret"))).ShouldSucceed();
        var file = await WriteFileAsync("c.json", $$"""{ "db": { "host": "{{host}}", "port": 5432 }, "token": "@{{pair}}" }""");
        (await Sform(At("story", "config", "set", key, "-i", file))).ShouldSucceed();

        await Assert.That(await ExportAsync("app.env", "-e", "app.env")).IsEqualTo($"DB__HOST={host}\nDB__PORT=5432\nTOKEN={secret}");
        await Assert.That(await ExportAsync("prefixed.env", "--export", "prefixed.env", "-f", "env", "--prefix", "APP_"))
            .IsEqualTo($"APP_DB__HOST={host}\nAPP_DB__PORT=5432\nAPP_TOKEN={secret}");

        var flat = JsonNode.Parse(await ExportAsync("app.flat", "-e", "app.flat", "--format", "flat"))!.AsObject();
        await Assert.That(flat.Count).IsEqualTo(3);
        await Assert.That(flat["db:host"]?.GetValue<string>()).IsEqualTo(host);
        await Assert.That(flat["db:port"]?.GetValue<string>()).IsEqualTo("5432");
        await Assert.That(flat["token"]?.GetValue<string>()).IsEqualTo(secret);

        await Assert.That(await ExportAsync("app.properties", "-e", "app.properties", "-f", "properties"))
            .IsEqualTo($"db.host={host}\ndb.port=5432\ntoken={secret}");
        await Assert.That(await ExportAsync("app.yaml", "-e", "app.yaml", "-f", "yaml"))
            .IsEqualTo($"db:\n  host: \"{host}\"\n  port: 5432\ntoken: \"{secret}\"");

        var json = JsonNode.Parse(await ExportAsync("app.json", "-e", "app.json", "-f", "json"))!;
        await Assert.That(json["Content"]).IsNull();
        await Assert.That(json["Version"]).IsNull();
        await Assert.That(json["db"]?["host"]?.GetValue<string>()).IsEqualTo(host);
        await Assert.That(json["db"]?["port"]?.GetValue<long>()).IsEqualTo(5432L);
        await Assert.That(json["token"]?.GetValue<string>()).IsEqualTo(secret);

        // Runs config export <rst> with the options, checks what it prints, and returns the file's lines joined with LF.
        async Task<string> ExportAsync(string path, params string[] options)
        {
            var export = (await Sform(At(["story", "config", "export", key, .. options])))
                .ShouldSucceed()
                .ShouldContain("Masked 1 value(s) (bound).", SformStream.StdErr)
                .ShouldContain($"Wrote the configuration to {path}.", SformStream.StdErr);
            await Assert.That(string.Join('\n', ConfigOutput.LinesOf(export.PlainStdOut))).IsEqualTo($"{AddMask}{secret}");
            return string.Join('\n', ConfigOutput.LinesOf(await ReadFileAsync(path)));
        }

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // The standard output of an export holds only ::add-mask:: lines (or none).
    private static bool OnlyMasks(SformResult result)
    {
        return ConfigOutput.LinesOf(result.PlainStdOut).All(line => line.StartsWith(AddMask, StringComparison.Ordinal));
    }
}
