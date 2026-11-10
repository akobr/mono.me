using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Language.Flow;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform story config restore <key> --version N [-f|--force] (424-221): the content of version N stored as the new version.
// Restored is 0, nothing changed 42; a refusal prints the server's message and code (a schema violation the failing
// configurations), and a 401 is left to Program.
public class ConfigRestoreCommandTests
{
    private const string Key = "rst.billing";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);

    private IReadOnlyList<string> Lines => _fixture.Console.Lines;

    [Fact]
    public async Task Restored_NamesTheNewVersion()
    {
        Restore(2, false).ReturnsAsync(Result(VersionRestoreResultOutcome.Restored, 2, 5));

        var exitCode = await Command(2).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe([$"! Restored v2 of the configuration for '{Key}' as v5."]);
    }

    [Fact]
    public async Task CurrentVersion_IsAlreadyCurrent()
    {
        Restore(5, false).ReturnsAsync(Result(VersionRestoreResultOutcome.Unchanged, 5, 5));

        var exitCode = await Command(5).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe([$"! v5 is already the current version of the configuration for '{Key}'."]);
    }

    [Fact]
    public async Task VersionWithTheCurrentContent_ChangesNothing()
    {
        Restore(3, false).ReturnsAsync(Result(VersionRestoreResultOutcome.Unchanged, 3, 5));

        var exitCode = await Command(3).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe([$"! v3 equals the current version v5 of the configuration for '{Key}'; nothing changed."]);
    }

    // a configuration written before versions were counted can list a version 0, so the API decides
    [Fact]
    public async Task VersionZero_GoesToTheApi()
    {
        Restore(0, false).ReturnsAsync(Result(VersionRestoreResultOutcome.Restored, 0, 4));

        var exitCode = await Command(0).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe([$"! Restored v0 of the configuration for '{Key}' as v4."]);
    }

    [Fact]
    public async Task Force_IsPassedThrough()
    {
        Restore(1, true).ReturnsAsync(Result(VersionRestoreResultOutcome.Restored, 1, 4));

        var exitCode = await Command(1, force: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.RestoreConfigurationVersionAsync("org1", "proj1", "default", Key, 1, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutVersion_SaysSoBeforeAnyCall()
    {
        var exitCode = await Command(null).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {VersionRestoreConsole.MissingVersionMessage}"]);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-7)]
    public async Task NegativeVersion_SaysSoBeforeAnyCall(int version)
    {
        var exitCode = await Command(version).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! The version to restore cannot be negative, not {version}; 'versions' lists them."]);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidKey_SaysSoBeforeAnyCall()
    {
        var command = Command(1);
        command.AnnotationKey = "not a key";

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines[0].ShouldBe("! Please specify a valid annotation key.");
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SchemaViolation_ListsTheConfigurations()
    {
        Restore(1, false).ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
            "The version violates the schema.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new SchemaValidationErrorResponse
            {
                Message = "The configuration does not comply with the schema.",
                Errors = [new SchemaValidationErrorDetail { AnnotationKey = Key, ViewName = "default", Errors = ["#/a: NumberExpected"] }],
            },
            null));

        var exitCode = await Command(1).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The configuration does not comply with the schema.", $"{Key} (default)", "  #/a: NumberExpected"]);
    }

    [Fact]
    public async Task UnknownVersion_PrintsTheServerMessageAndCode()
    {
        Restore(9, false).ThrowsAsync(AnnotationCommandFixture.Error(404, "VersionNotFound", $"The version 9 of the configuration of '{Key}' does not exist or has expired."));

        var exitCode = await Command(9).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! The version 9 of the configuration of '{Key}' does not exist or has expired.", "Error code: VersionNotFound"]);
    }

    [Fact]
    public async Task DeletedAnnotation_PrintsTheServerMessageAndCode()
    {
        // the client reads every 409 of the restore as a SchemaValidationErrorResponse, without entries for a deleted annotation
        Restore(1, false).ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
            "The version violates the schema.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new SchemaValidationErrorResponse { Message = $"The annotation '{Key}' is deleted.", ErrorCode = "AnnotationDeleted" },
            null));

        var exitCode = await Command(1).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! The annotation '{Key}' is deleted.", "Error code: AnnotationDeleted"]);
    }

    [Fact]
    public async Task Unauthorized_IsLeftToProgram()
    {
        Restore(1, false).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => Command(1).OnExecuteAsync());

        exception.StatusCode.ShouldBe(401);
        Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Options_AreParsed()
    {
        var command = Parse("restore", Key, "--version", "3", "-f");

        command.AnnotationKey.ShouldBe(Key);
        command.Version.ShouldBe(3);
        command.Force.ShouldBeTrue();
        Parse("restore", Key, "--version", "2", "--force").Force.ShouldBeTrue();
        Parse("restore", Key, "--version=4").Version.ShouldBe(4);
        Parse("restore", Key, "--version", "-1").Version.ShouldBe(-1);
        Parse("restore", Key).Version.ShouldBeNull();
        Parse("restore", Key, "--version", "1").Force.ShouldBeFalse();
    }

    [Fact]
    public void ShortVersion_IsTheView()
    {
        var command = Parse("restore", Key, "-v", "staging");

        command.ViewName.ShouldBe("staging");
        command.Version.ShouldBeNull();
    }

    [Fact]
    public async Task ParsedCommand_RestoresTheVersion()
    {
        Restore(2, true).ReturnsAsync(Result(VersionRestoreResultOutcome.Restored, 2, 3));

        var exitCode = await Parse("restore", Key, "--version", "2", "--force").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe([$"! Restored v2 of the configuration for '{Key}' as v3."]);
    }

    internal static VersionRestoreResult Result(VersionRestoreResultOutcome outcome, long fromVersion, long version)
    {
        return new VersionRestoreResult
        {
            Outcome = outcome,
            FromVersion = fromVersion,
            Version = version,
            Modified = new AuditStamp { At = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero) },
        };
    }

    private ISetup<IConfigurationsApiClient, Task<VersionRestoreResult>> Restore(int version, bool force)
    {
        return _api.Setup(api => api.RestoreConfigurationVersionAsync("org1", "proj1", "default", Key, version, force, It.IsAny<CancellationToken>()));
    }

    private ConfigRestoreCommand Command(int? version, bool force = false)
    {
        return new ConfigRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationKey = Key,
            Version = version,
            Force = force,
        };
    }

    // The command that config restore selects, as the host builds it.
    private ConfigRestoreCommand Parse(params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<ConfigRestoreCommand>>().Model;
    }
}
