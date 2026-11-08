using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
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
}
