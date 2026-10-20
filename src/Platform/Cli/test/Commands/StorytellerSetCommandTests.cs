using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class StorytellerSetCommandTests
{
    private const string Key = "exe.northwind.billing.prod";

    // the key and its ancestors, roots first, as the lookup receives them
    private static readonly string[] LookedUp =
    [
        "rst.billing",
        "sbt.northwind",
        "cnt.northwind.prod",
        "usg.northwind.billing",
        Key,
    ];

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly List<string> _calls = [];

    public StorytellerSetCommandTests()
    {
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>()))
            .Callback(() => _calls.Add("set"))
            .ReturnsAsync(AnnotationCommandFixture.Active(Key));
    }

    [Fact]
    public async Task NothingDeleted_WritesWithoutAsking()
    {
        DeletedAmong();

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["set"]);
        _fixture.Console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<string>>()), Times.Never);
    }

    [Fact]
    public async Task DeletedKey_WithRestoreDeleted_RestoresItAloneThenUpdates()
    {
        DeletedAmong(Key);
        Restores();

        var exitCode = await Execute(restoreDeleted: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe([$"restore:{Key}:False", "set"]);
        _fixture.Line("- exe.northwind.billing.prod").ShouldContain("by Ales Kobr");
    }

    [Fact]
    public async Task DeletedAncestor_WithRecreate_WritesWithoutRestoring()
    {
        DeletedAmong("sbt.northwind");

        var exitCode = await Execute(recreate: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["set"]);
        _fixture.Line("This annotation is deleted").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(StorytellerSetCommand.RestoreAndUpdateChoice, "restore:sbt.northwind:False,set")]
    [InlineData(StorytellerSetCommand.CreateNewChoice, "set")]
    public async Task DeletedAncestor_AsksAndFollowsTheAnswer(string answer, string expectedCalls)
    {
        DeletedAmong("sbt.northwind");
        Restores();
        _fixture.Console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns(answer);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        string.Join(",", _calls).ShouldBe(expectedCalls);
    }

    [Fact]
    public async Task DeletedKey_Cancelled_WritesNothing()
    {
        DeletedAmong(Key);
        _fixture.Console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns(StorytellerSetCommand.CancelChoice);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletedKey_WithRedirectedInputAndNoOption_StopsAndNamesBothOptions()
    {
        DeletedAmong(Key);
        _fixture.InnerConsole.SetupGet(console => console.IsInputRedirected).Returns(true);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBeEmpty();
        _fixture.Line("Choose").ShouldContain("--restore-deleted");
        _fixture.Line("Choose").ShouldContain("--recreate");
        _fixture.Console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<string>>()), Times.Never);
    }

    [Fact]
    public async Task FailingLookup_PrintsTheServerMessage()
    {
        _fixture.Api
            .Setup(api => api.FindDeletedAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationKeysRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidKey", "Too many annotation keys."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("Too many annotation keys.");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletedKey_WithRestoreDeletedAndWithoutTheManagementScope_PrintsTheServerMessageAndWritesNothing()
    {
        DeletedAmong(Key);
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.MissingManagementScope("Annotation.Manage"));

        var exitCode = await Execute(restoreDeleted: true);

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _calls.ShouldBeEmpty();
        _fixture.Console.Lines.ShouldContain($"! {AnnotationCommandFixture.MissingManagementScopeMessage("Annotation.Manage")}");
        _fixture.Console.Lines.ShouldContain("Error code: AccessDenied");
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BothOptions_AreRejected()
    {
        var exitCode = await Execute(restoreDeleted: true, recreate: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidAnnotation_PrintsTheServerMessage()
    {
        DeletedAmong();
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidAnnotation", "Title has 201 characters, at most 200 are allowed."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("Title has 201 characters, at most 200 are allowed.");
    }

    private void DeletedAmong(params string[] deletedKeys)
    {
        _fixture.Api
            .Setup(api => api.FindDeletedAnnotationsAsync(
                "org1",
                "proj1",
                "default",
                It.Is<AnnotationKeysRequest>(request => request.Keys.OrderBy(key => key).SequenceEqual(LookedUp.OrderBy(key => key)))))
            .ReturnsAsync(deletedKeys.Select(key => AnnotationCommandFixture.Deleted(key)).ToList());
    }

    private void Restores()
    {
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .Callback<string, string, string, AnnotationRestoreRequest>((_, _, _, request) =>
                _calls.Add($"restore:{string.Join(",", request.Keys)}:{request.Descendants}"))
            .ReturnsAsync((string _, string _, string _, AnnotationRestoreRequest request) => new AnnotationRestoreResult
            {
                Annotations = request.Keys.Select(AnnotationCommandFixture.Active).ToList(),
                CreatedKeys = [],
                Failed = [],
            });
    }

    private Task<int> Execute(bool restoreDeleted = false, bool recreate = false)
    {
        var command = new StorytellerSetCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            _fixture.Api.Object,
            Options.Create(new AccessDefaultOptions()),
            Mock.Of<IFileSystem>())
        {
            AnnotationKey = Key,
            RestoreDeleted = restoreDeleted,
            Recreate = recreate,
        };

        return command.OnExecuteAsync();
    }
}
