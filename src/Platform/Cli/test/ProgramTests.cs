using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.GitHubActions;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

// What Program.Main ends a run with when the command line cannot be parsed: the input is wrong, the tool is not broken.
public class ProgramTests
{
    private const string Key = "exe.northwind.invoicing.prod";

    // --mask, --github-env and --allow-unprefixed are options of config export only; config refuses them as unknown.
    [Theory]
    [InlineData("--format")]
    [InlineData("--format", "env", "--format", "yaml")]
    [InlineData("--version", "abc")]
    [InlineData("--mask")]
    [InlineData("--github-env")]
    [InlineData("--allow-unprefixed")]
    [InlineData("--bogus")]
    public async Task RunAsync_OfACommandLineThatDoesNotParse_IsInputParsing(params string[] options)
    {
        string[] args = [Key, .. options];

        var exitCode = await RunAsync(args);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
    }

    [Theory]
    [InlineData("--mask")]
    [InlineData("--prefix")]
    [InlineData("--format", "env", "--format", "yaml")]
    [InlineData("--resolved")]
    [InlineData("--env-prefix", "APP_")]
    [InlineData("--bogus")]
    public async Task RunAsync_OfAnExportThatDoesNotParse_IsInputParsing(params string[] options)
    {
        string[] args = ["export", Key, .. options];

        var exitCode = await RunAsync(args);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
    }

    [Theory]
    [InlineData("env-prefix", Key, "--env-prefix")]
    [InlineData("prefix", "export", Key, "--github-env", "--prefix")]
    [InlineData("mask", "export", Key, "--mask")]
    public void Parse_OfAMissingValue_ThrowsACommandParsingException(string option, params string[] args)
    {
        Should.Throw<CommandParsingException>(() => Parse(args)).Message.ShouldBe($"Missing value for option '{option}'");
    }

    private static Task<int> RunAsync(string[] args)
    {
        return Program.RunAsync(
            () =>
            {
                Parse(args);
                return Task.FromResult(ExitCodes.SUCCESS);
            },
            args);
    }

    // A command outside BaseContextCommand that stopped at a prompt on redirected input: it wrote why, Main ends with its code.
    [Theory]
    [InlineData(ExitCodes.WARNING_INTERACTION_NEEDED)]
    [InlineData(ExitCodes.ERROR_WRONG_INPUT)]
    public async Task RunAsync_OfAnOutputException_EndsWithItsExitCode(int code)
    {
        var exitCode = await Program.RunAsync(() => throw new InteractionNeededException(code, "sform cannot ask for the default project."), []);

        exitCode.ShouldBe(code);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("./state")]
    public void ResolvePaths_OfARelativeHome_SaysWhyAndGivesNoPaths(string home)
    {
        var error = new StringWriter();

        Program.ResolvePaths(name => name == "SFORM_HOME" ? home : null, new MockFileSystem(), error).ShouldBeNull();

        error.ToString().ShouldBe($"! SFORM_HOME must be an absolute path, and '{home}' is relative.{Environment.NewLine}");
    }

    [Fact]
    public void ResolvePaths_OfAnAbsoluteHome_GivesItsPaths()
    {
        var home = MockUnixSupport.Path(@"C:\state\ada");
        var error = new StringWriter();

        var paths = Program.ResolvePaths(name => name == "SFORM_HOME" ? home : null, new MockFileSystem(), error);

        paths.ShouldNotBeNull().StateDirectory.ShouldBe(home);
        paths.UserDirectory.ShouldBe(home);
        error.ToString().ShouldBeEmpty();
    }

    // GEN-CLI-04: an API that does not answer is a service failure, not a crash.
    [Fact]
    public async Task RunAsync_OfAnUnreachableService_IsAServiceFailure()
    {
        var exitCode = await Program.RunAsync(
            () => throw new HttpRequestException("No connection could be made because the target machine actively refused it. (127.0.0.1:9)"),
            []);

        exitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE);
    }

    [Fact]
    public async Task RunAsync_OfATimedOutRequest_IsAServiceFailure()
    {
        var exitCode = await Program.RunAsync(
            () => throw new TaskCanceledException("The request was canceled.", new TimeoutException("The operation was canceled.")),
            []);

        exitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE);
    }

    // A release build is Production whatever DOTNET_ENVIRONMENT says; a build from source follows it, else Development.
    [Theory]
    [InlineData(true, null, "Development")]
    [InlineData(true, "  ", "Development")]
    [InlineData(true, "Production", "Production")]
    [InlineData(true, " Staging ", "Staging")]
    [InlineData(false, null, "Production")]
    [InlineData(false, "Development", "Production")]
    public void ResolveEnvironmentName_FollowsDotnetEnvironmentInASourceBuildOnly(bool isSourceBuild, string? variable, string expected)
    {
        Program.ResolveEnvironmentName(name => name == "DOTNET_ENVIRONMENT" ? variable : null, isSourceBuild).ShouldBe(expected);
    }

    // The command line of sform story config and its subcommands, as the host builds it.
    private static void Parse(params string[] args)
    {
        var fixture = new AnnotationCommandFixture();
        var services = new ServiceCollection()
            .AddSingleton(fixture.Console.Object)
            .AddSingleton(fixture.Context.Object)
            .AddSingleton(Mock.Of<IConfigurationsApiClient>())
            .AddSingleton(Mock.Of<IFileSystem>())
            .AddSingleton(GitHubActionsSettings.FromEnvironment(_ => null))
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);
        app.Parse(args);
    }

    // A request that failed without an answer names its service when it was not the API.
    [Theory]
    [InlineData("https://auth.example.com", "https://api.example.com/api", "https://auth.example.com")]
    [InlineData("https://API.example.com", "https://api.example.com/api", null)]
    [InlineData("http://localhost:7071", "http://localhost:7071/api", null)]
    [InlineData(null, "https://api.example.com/api", null)]
    [InlineData("https://auth.example.com", null, null)]
    public void UnreachableTargetOf_IsTheServiceOtherThanTheApi(string? lastTarget, string? baseUrl, string? expected)
    {
        Program.UnreachableTargetOf(lastTarget, baseUrl).ShouldBe(expected);
    }
}
