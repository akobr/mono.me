using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Templates;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Language.Flow;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform story template restore <type> --version N (424-221): the content of version N stored as the new version of the
// template. A template has no schema, so no --force.
public class TemplateRestoreCommandTests
{
    private const string Type = "unt";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<ITemplatesApiClient> _api = new(MockBehavior.Strict);

    private IReadOnlyList<string> Lines => _fixture.Console.Lines;

    [Fact]
    public async Task Restored_NamesTheNewVersion()
    {
        Restore(1).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Restored, 1, 3));

        var exitCode = await Command(1).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe(["! Restored v1 of the template for 'unt' as v3."]);
    }

    [Fact]
    public async Task CurrentVersion_IsAlreadyCurrent()
    {
        Restore(3).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Unchanged, 3, 3));

        var exitCode = await Command(3).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe(["! v3 is already the current version of the template for 'unt'."]);
    }

    [Fact]
    public async Task VersionWithTheCurrentContent_ChangesNothing()
    {
        Restore(1).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Unchanged, 1, 3));

        var exitCode = await Command(1).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe(["! v1 equals the current version v3 of the template for 'unt'; nothing changed."]);
    }

    [Fact]
    public async Task WithoutVersion_SaysSoBeforeAnyCall()
    {
        var exitCode = await Command(null).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {VersionRestoreConsole.MissingVersionMessage}"]);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NegativeVersion_SaysSoBeforeAnyCall()
    {
        var exitCode = await Command(-2).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The version to restore cannot be negative, not -2; 'versions' lists them."]);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownType_SaysSoBeforeAnyCall()
    {
        var command = Command(1);
        command.AnnotationType = "xyz";

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines[0].ShouldBe("! Please specify a valid annotation type code.");
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(404, "VersionNotFound", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "Conflict", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, "AccessDenied", ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(500, "Unexpected", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task ApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, int expected)
    {
        Restore(7).ThrowsAsync(AnnotationCommandFixture.Error(status, errorCode, "The server refused it."));

        var exitCode = await Command(7).OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldBe(["! The server refused it.", $"Error code: {errorCode}"]);
    }

    [Fact]
    public async Task Unauthorized_IsLeftToProgram()
    {
        Restore(1).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => Command(1).OnExecuteAsync());

        exception.StatusCode.ShouldBe(401);
        Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Options_AreParsed()
    {
        var command = Parse("restore", Type, "--version", "2");

        command.AnnotationType.ShouldBe(Type);
        command.Version.ShouldBe(2);
        Parse("restore", Type).Version.ShouldBeNull();
        Should.Throw<UnrecognizedCommandParsingException>(() => Parse("restore", Type, "--version", "2", "-f"));
    }

    private ISetup<ITemplatesApiClient, Task<VersionRestoreResult>> Restore(int version)
    {
        return _api.Setup(api => api.RestoreTemplateVersionAsync("org1", "proj1", "default", Type, version, It.IsAny<CancellationToken>()));
    }

    private TemplateRestoreCommand Command(int? version)
    {
        return new TemplateRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationType = Type,
            Version = version,
        };
    }

    // The command that template restore selects, as the host builds it.
    private TemplateRestoreCommand Parse(params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .BuildServiceProvider();
        var app = new CommandLineApplication<TemplateGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<TemplateRestoreCommand>>().Model;
    }
}
