using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// GitHub Actions and Masking: --github-env, --env-prefix, --mask and the --allow-* options of story config.
[Area(Area.Configurations)]
public sealed class ConfigGitHubTests : CliTest
{
    // CFG-GHA-01: --github-env with --env-prefix writes the resolved values into GITHUB_ENV and masks bound values.
    // As a ContributorWithSecrets, a secret pair and a configuration of <rst> with a plain value and a binding to the
    // secret; story config <rst> -r --github-env --env-prefix APP_ with GITHUB_ENV naming an empty file writes APP_PLAIN
    // and APP_TOKEN there in the heredoc form (NAME<<ghadelimiter_…), prints ::add-mask:: for the bound secret only, and
    // prints no content (neither value is on the standard output but in that mask).
    [Test]
    [TestCase(436)]
    [Elementary]
    [UserRole(Role.Secrets)]
    [Covers("story config", "--github-env", "--env-prefix")]
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
            InProject("story", "config", key, "-r", "--github-env", "--env-prefix", "APP_"));

        export.ShouldSucceed()
            .ShouldContain($"::add-mask::{secret}")
            .ShouldNotContain($"::add-mask::{plain}")
            .ShouldNotContain(plain);
        await Assert.That(export.PlainStdOut.Replace($"::add-mask::{secret}", string.Empty, StringComparison.Ordinal)).DoesNotContain(secret);

        var written = await ReadFileAsync(githubEnv);
        await Assert.That(written).Matches(@"(?m)^APP_PLAIN<<ghadelimiter_[0-9a-f]{32}$");
        await Assert.That(written).Matches(@"(?m)^APP_TOKEN<<ghadelimiter_[0-9a-f]{32}$");
        await Assert.That(written).Contains($"\n{plain}\n");
        await Assert.That(written).Contains($"\n{secret}\n");
    }

    // CFG-GHA-02: --github-env without --env-prefix fails, and --allow-unprefixed allows it.
    // As a ContributorWithSecrets, with plain=<v> stored on <rst> and GITHUB_ENV naming an empty file: config <rst> -r
    // --github-env ends with 2 before any call, naming --env-prefix and --allow-unprefixed on the standard error, and
    // writes nothing; with --allow-unprefixed it exports the names without a prefix (PLAIN<<ghadelimiter_…, the value)
    // and says "Exported … to GITHUB_ENV: … PLAIN …" on the standard error.
    [Test]
    [TestCase(437)]
    [UserRole(Role.Secrets)]
    [Covers("story config", "--allow-unprefixed")]
    public async Task GitHubEnv_Unprefixed_NeedsAllow()
    {
        var key = CliData.ResponsibilityKey();
        var plain = CliData.Value();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}"))).ShouldSucceed();
        var githubEnv = await WriteFileAsync("github_env", string.Empty);
        var options = new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } };

        (await Sform(options, InProject("story", "config", key, "-r", "--github-env")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("--github-env needs --env-prefix", SformStream.StdErr)
            .ShouldContain("--allow-unprefixed", SformStream.StdErr);
        await Assert.That(await ReadFileAsync(githubEnv)).IsEmpty();

        (await Sform(options, InProject("story", "config", key, "-r", "--github-env", "--allow-unprefixed")))
            .ShouldSucceed()
            .ShouldContain("to GITHUB_ENV:", SformStream.StdErr)
            .ShouldContain("PLAIN", SformStream.StdErr);
        var written = await ReadFileAsync(githubEnv);
        await Assert.That(ConfigOutput.GitHubEnvNames(written)).Contains("PLAIN");
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "PLAIN")).IsEqualTo(plain);
    }

    // CFG-GHA-03: --mask bound and --mask all print ::add-mask:: for bound values or every value.
    // As a ContributorWithSecrets, with a secret pair and a configuration of <rst> with plain=<v>, count=12345,
    // flag=true and token=@<secret pair>: config <rst> -r --mask bound prints ::add-mask:: for the bound secret only
    // ("Masked … (bound)." on the standard error); --mask all prints it for every string and number (the plain value,
    // 12345 and the secret), never for the boolean ("Masked … (all).").
    [Test]
    [TestCase(438)]
    [UserRole(Role.Secrets)]
    [Covers("story config", "--mask", "--mask=bound", "--mask=all")]
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

        (await Sform(InProject("story", "config", key, "-r", "--mask", "bound")))
            .ShouldSucceed()
            .ShouldContain($"::add-mask::{secret}")
            .ShouldNotContain($"::add-mask::{plain}")
            .ShouldNotContain("::add-mask::12345")
            .ShouldNotContain("::add-mask::true")
            .ShouldContain("value(s) (bound).", SformStream.StdErr);

        (await Sform(InProject("story", "config", key, "-r", "--mask", "all")))
            .ShouldSucceed()
            .ShouldContain($"::add-mask::{secret}")
            .ShouldContain($"::add-mask::{plain}")
            .ShouldContain("::add-mask::12345")
            .ShouldNotContain("::add-mask::true")
            .ShouldContain("value(s) (all).", SformStream.StdErr);
    }

    // CFG-GHA-04: An unresolved binding fails the export, and --allow-unresolved exports it as it is.
    // As a ContributorWithSecrets, with a configuration of <rst> with plain=<v> and missing=@<a key that does not exist>
    // (a missing key declines, so the resolved value stays the expression) and GITHUB_ENV naming an empty file: config
    // <rst> -r --github-env --env-prefix A_ ends with 2 naming the binding (missing) and --allow-unresolved on the standard
    // error, and writes nothing; with --allow-unresolved it exports A_MISSING with the expression as it is and A_PLAIN,
    // and notes the unresolved binding on the standard error.
    [Test]
    [TestCase(439)]
    [UserRole(Role.Secrets)]
    [Covers("story config", "--allow-unresolved")]
    public async Task GitHubEnv_Unresolved_NeedsAllow()
    {
        var key = CliData.ResponsibilityKey();
        var missing = CliData.KeyValueKey();
        var plain = CliData.Value();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"plain={plain}", "-x", $"missing=@{missing}"))).ShouldSucceed();
        var githubEnv = await WriteFileAsync("github_env", string.Empty);
        var options = new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } };

        (await Sform(options, InProject("story", "config", key, "-r", "--github-env", "--env-prefix", "A_")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("These values are binding expressions that were not resolved:", SformStream.StdErr)
            .ShouldContain("missing", SformStream.StdErr)
            .ShouldContain("--allow-unresolved", SformStream.StdErr);
        await Assert.That(await ReadFileAsync(githubEnv)).IsEmpty();

        (await Sform(options, InProject("story", "config", key, "-r", "--github-env", "--env-prefix", "A_", "--allow-unresolved")))
            .ShouldSucceed()
            .ShouldContain("Binding expressions that were not resolved, exported as they are:", SformStream.StdErr)
            .ShouldContain("missing", SformStream.StdErr);
        var written = await ReadFileAsync(githubEnv);
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "A_MISSING")).IsEqualTo($"@{missing}");
        await Assert.That(ConfigOutput.GitHubEnvValueOf(written, "A_PLAIN")).IsEqualTo(plain);
    }

    // CFG-GHA-05: --github-env or --mask without -r fails with exit 2.
    // As a Reader, config <rst> --github-env --env-prefix A_ and config <rst> --mask all end with 2 before any call, with
    // "--github-env and --mask export the resolved configuration: add --resolved, without --version." on the standard
    // error and nothing on the standard output.
    [Test]
    [TestCase(440)]
    [UserRole(Role.Reader)]
    [Covers("story config", "--github-env", "--mask")]
    public async Task GitHubEnv_WithoutResolved_Fails()
    {
        var key = CliData.ResponsibilityKey();
        string[][] runs = [["--github-env", "--env-prefix", "A_"], ["--mask", "all"]];

        foreach (var options in runs)
        {
            var refused = (await Sform(InProject(["story", "config", key, .. options])))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain("--github-env and --mask export the resolved configuration: add --resolved, without --version.", SformStream.StdErr);
            await Assert.That(refused.PlainStdOut.Trim()).IsEmpty();
        }
    }
}
