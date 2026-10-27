using System;
using System.Linq;
using System.Text.RegularExpressions;
using _42.Platform.Cli.GitHubActions;
using _42.Platform.Storyteller.ConfigurationFormats;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.GitHubActions;

// The variables sform appends to GITHUB_ENV: the names it never writes, the values it refuses, and the heredocs.
public class GitHubEnvironmentFileTests
{
    // The names and prefixes of the spec, section 7, step 3.
    private static readonly string[] Names =
    [
        "NODE_OPTIONS", "PATH", "HOME", "SHELL", "ENV", "BASH_ENV", "CDPATH", "IFS", "PS4", "PROMPT_COMMAND", "SHELLOPTS",
        "BASHOPTS", "GLOBIGNORE", "CLASSPATH", "CC", "CXX", "MAKEFLAGS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS",
        "JDK_JAVA_OPTIONS", "JAVA_OPTS", "MAVEN_OPTS", "GRADLE_OPTS", "SBT_OPTS", "PYTHONPATH", "PYTHONSTARTUP",
        "PYTHONHOME", "PERL5OPT", "PERL5LIB", "PERLLIB", "RUBYOPT", "RUBYLIB", "NODE_PATH", "NODE_EXTRA_CA_CERTS",
        "NODE_TLS_REJECT_UNAUTHORIZED", "LUA_INIT", "RUSTC_WRAPPER", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "GOFLAGS", "GOPROXY", "GONOSUMDB",
        "GOSUMDB", "GOINSECURE", "RESTORESOURCES", "KUBECONFIG", "PATHEXT", "COMSPEC", "PSMODULEPATH",
    ];

    private static readonly string[] Prefixes =
    [
        "GITHUB_", "RUNNER_", "ACTIONS_", "INPUT_", "STATE_", "LD_", "DYLD_", "BASH_FUNC_", "DOTNET_", "COREHOST_",
        "CORECLR_", "COMPLUS_", "MSBUILD", "NUGET_", "NPM_CONFIG_", "YARN_", "PIP_", "CARGO_", "DOCKER_", "GIT_",
        "STORYTELLER_",
    ];

    public static TheoryData<string> ReservedNames => new(Names);

    public static TheoryData<string> ReservedPrefixes => new(Prefixes);

    [Theory]
    [MemberData(nameof(ReservedNames))]
    public void IsReserved_ForEveryNameOfTheList_InAnyCase(string name)
    {
        GitHubEnvironmentFile.IsReserved(name).ShouldBeTrue();
        GitHubEnvironmentFile.IsReserved(name.ToLowerInvariant()).ShouldBeTrue();
        GitHubEnvironmentFile.IsReserved($"APP_{name}").ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ReservedPrefixes))]
    public void IsReserved_ForEveryNameThatStartsWithAPrefixOfTheList(string prefix)
    {
        GitHubEnvironmentFile.IsReserved(prefix).ShouldBeTrue();
        GitHubEnvironmentFile.IsReserved($"{prefix}X").ShouldBeTrue();
        GitHubEnvironmentFile.IsReserved($"{prefix.ToLowerInvariant()}x").ShouldBeTrue();
        GitHubEnvironmentFile.IsReserved($"APP_{prefix}X").ShouldBeFalse();
    }

    [Theory]
    [InlineData("APP_PATH")]
    [InlineData("PATHS")]
    [InlineData("DB__HOST")]
    [InlineData("DOTNET")]
    [InlineData("GITHUB")]
    public void IsReserved_NotForOtherNames(string name)
    {
        GitHubEnvironmentFile.IsReserved(name).ShouldBeFalse();
    }

    [Fact]
    public void FindRefusals_OfVariablesThatFit_IsEmpty()
    {
        GitHubEnvironmentFile.FindRefusals([Variable("DB__HOST", "db.local"), Variable("EMPTY", string.Empty)]).ShouldBeEmpty();
    }

    [Fact]
    public void FindRefusals_NameNamesAndPaths_NeverAValue()
    {
        var refusals = GitHubEnvironmentFile.FindRefusals(
        [
            Variable("PATH", "s3cr3t-value"),
            Variable("NUL", "s3cr3t\0value"),
            Variable("LONG", new string('s', GitHubEnvironmentFile.MaxVariableLength)),
        ]);

        refusals.Count.ShouldBe(3);
        refusals.ShouldAllBe(refusal => !refusal.Contains("s3cr3t", StringComparison.Ordinal) && !refusal.Contains("sss", StringComparison.Ordinal));
        refusals[0].ShouldContain("PATH ($.path)");
    }

    [Fact]
    public void Build_WritesOneHeredocPerVariable_WithLfLineEnds()
    {
        var text = GitHubEnvironmentFile.Build(
            [Variable("A", "x"), Variable("B", "two\r\nlines"), Variable("C", string.Empty)],
            () => "ghadelimiter_1");

        text.ShouldBe("A<<ghadelimiter_1\nx\nghadelimiter_1\nB<<ghadelimiter_1\ntwo\r\nlines\nghadelimiter_1\nC<<ghadelimiter_1\n\nghadelimiter_1\n");
    }

    [Fact]
    public void Build_TakesAnotherDelimiter_UntilNoValueContainsIt()
    {
        var delimiters = new[] { "ghadelimiter_1", "ghadelimiter_2", "ghadelimiter_3" }.AsEnumerable().GetEnumerator();

        var text = GitHubEnvironmentFile.Build(
            [Variable("A", "ghadelimiter_1"), Variable("B", "x ghadelimiter_2 y")],
            () => delimiters.MoveNext() ? delimiters.Current : throw new InvalidOperationException());

        text.ShouldBe("A<<ghadelimiter_3\nghadelimiter_1\nghadelimiter_3\nB<<ghadelimiter_3\nx ghadelimiter_2 y\nghadelimiter_3\n");
    }

    [Fact]
    public void Build_OfNoVariables_IsEmpty()
    {
        GitHubEnvironmentFile.Build([], () => "ghadelimiter_1").ShouldBeEmpty();
    }

    [Fact]
    public void NewDelimiter_IsRandom_AsTheRunnersOwn()
    {
        var settings = GitHubActionsSettings.FromEnvironment(_ => null);

        var first = settings.NewDelimiter();
        var second = settings.NewDelimiter();

        first.ShouldMatch("^ghadelimiter_[0-9a-f]{32}$");
        second.ShouldNotBe(first);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("/home/runner/work/_temp/_runner_file_commands/set_env_1", "/home/runner/work/_temp/_runner_file_commands/set_env_1")]
    public void FromEnvironment_ReadsGitHubEnv_AndTreatsEmptyAsUnset(string? value, string? expected)
    {
        var settings = GitHubActionsSettings.FromEnvironment(name => name == "GITHUB_ENV" ? value : "other");

        settings.EnvironmentFile.ShouldBe(expected);
    }

    [Theory]
    [InlineData("s3cr3t", "s3cr3t")]
    [InlineData("50%off", "50%25off")]
    [InlineData("a\r\nb\nc", "a%0D%0Ab%0Ac")]
    [InlineData("%0A", "%250A")]
    public void AddMask_EscapesTheData_AsTheRunnerReadsIt(string value, string data)
    {
        var command = WorkflowCommands.AddMask(value);

        command.ShouldBe($"::add-mask::{data}\n");
        Regex.Matches(command, "\n").Count.ShouldBe(1);
    }

    private static EnvironmentVariable Variable(string name, string value)
    {
        return new EnvironmentVariable(name, value, DocumentValueKind.String, $"$.{name.ToLowerInvariant()}");
    }
}
