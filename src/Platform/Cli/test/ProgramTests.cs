using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.GitHubActions;
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

    [Theory]
    [InlineData("--format")]
    [InlineData("--format", "env", "--format", "yaml")]
    [InlineData("--version", "abc")]
    [InlineData("--mask")]
    [InlineData("--bogus")]
    public async Task RunAsync_OfACommandLineThatDoesNotParse_IsInputParsing(params string[] options)
    {
        string[] args = [Key, .. options];

        var exitCode = await Program.RunAsync(
            () =>
            {
                Parse(args);
                return Task.FromResult(ExitCodes.SUCCESS);
            },
            args);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
    }

    [Fact]
    public void Parse_OfAMissingValue_ThrowsACommandParsingException()
    {
        Should.Throw<CommandParsingException>(() => Parse(Key, "--env-prefix")).Message.ShouldBe("Missing value for option 'env-prefix'");
    }

    // The command line of sform story config, as the host builds it.
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
}
