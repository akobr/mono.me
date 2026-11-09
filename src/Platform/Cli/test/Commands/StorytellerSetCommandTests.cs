using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class StorytellerSetCommandTests
{
    private const string Key = "exe.northwind.billing.prod";
    private const string ETag = "\"a1.djE\"";

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
        // The merge: one patch, without a read.
        Patches();

        // The create of a missing key, only while it is missing.
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>(), null, "*"))
            .Callback(() => _calls.Add("create"))
            .ReturnsAsync(AnnotationCommandFixture.Active(Key));

        // The read and the write of --replace, over the version read.
        _fixture.Api
            .Setup(api => api.GetAnnotationVersionedAsync("org1", "proj1", "default", Key, true))
            .Callback(() => _calls.Add("get"))
            .ReturnsAsync(Versioned(ETag));
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>(), ETag, null))
            .Callback(() => _calls.Add("replace"))
            .ReturnsAsync(AnnotationCommandFixture.Active(Key));
    }

    [Fact]
    public async Task NothingDeleted_PatchesWithoutAskingOrReading()
    {
        DeletedAmong();

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["patch"]);
        _fixture.Console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<string>>()), Times.Never);
    }

    [Fact]
    public async Task MissingKey_IsCreatedAfterThePatch()
    {
        DeletedAmong();
        Patches(AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["patch", "create"]);
    }

    [Fact]
    public async Task KeyCreatedMeanwhile_IsPatchedInTheNextRound()
    {
        DeletedAmong();
        Patches(AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."), null);
        CreateFails(412, "AnnotationPreconditionFailed", "The annotation exists already.");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["patch", "create", "patch"]);
    }

    [Fact]
    public async Task ThreeRoundsLost_StopWithoutAnotherRequest()
    {
        DeletedAmong();
        Patches(Enumerable.Repeat<Exception?>(AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."), StorytellerSetCommand.MaxWriteRounds).ToArray());
        CreateFails(412, "AnnotationPreconditionFailed", "The annotation exists already.");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _calls.ShouldBe(["patch", "create", "patch", "create", "patch", "create"]);
        _fixture.Console.Lines.ShouldContain($"The annotation {Key} changed concurrently; try again.");
    }

    [Fact]
    public async Task KeyDeletedAfterTheLookup_IsNotReCreated()
    {
        DeletedAmong();
        Patches(AnnotationCommandFixture.Error(404, "AnnotationDeleted", "The annotation is deleted."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _calls.ShouldBe(["patch"]);
        _fixture.Line($"The annotation {Key} was deleted meanwhile").ShouldContain("run the command again");
    }

    [Fact]
    public async Task DeletedKey_WithRestoreDeleted_RestoresItAloneThenPatches()
    {
        DeletedAmong(Key);
        Restores();

        var exitCode = await Execute(restoreDeleted: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe([$"restore:{Key}:False", "patch"]);
        _fixture.Line("- exe.northwind.billing.prod").ShouldContain("by Ales Kobr");
    }

    [Fact]
    public async Task DeletedAncestor_WithRecreate_CreatesWithoutRestoringOrPatching()
    {
        DeletedAmong("sbt.northwind");

        var exitCode = await Execute(recreate: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["create"]);
        _fixture.Line("This annotation is deleted").ShouldNotBeNull();
    }

    [Fact]
    public async Task DeletedKey_WithRecreate_RestoredMeanwhile_IsPatched()
    {
        DeletedAmong(Key);
        CreateFails(412, "AnnotationPreconditionFailed", "The annotation exists already.");

        var exitCode = await Execute(recreate: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["create", "patch"]);
    }

    [Theory]
    [InlineData(StorytellerSetCommand.RestoreAndUpdateChoice, "restore:sbt.northwind:False,patch")]
    [InlineData(StorytellerSetCommand.CreateNewChoice, "create")]
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
        _fixture.Console.UseInnerConsole(redirectedInput: true);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBeEmpty();
        _fixture.Console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for what happens to the deleted annotations: the input is redirected.",
            "  Choose with an option: --restore-deleted restores and updates them, --recreate creates them new.",
        ]);
        _fixture.Console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<string>>()), Times.Never);
    }

    [Fact]
    public async Task Replace_ReadsTheVersion_AndWritesOnlyIt()
    {
        DeletedAmong();

        var exitCode = await Execute(replace: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "replace"]);
    }

    [Fact]
    public async Task Replace_OfAMissingKey_OnlyCreates()
    {
        DeletedAmong();
        _fixture.Api
            .Setup(api => api.GetAnnotationVersionedAsync("org1", "proj1", "default", Key, true))
            .Callback(() => _calls.Add("get"))
            .ThrowsAsync(AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."));

        var exitCode = await Execute(replace: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "create"]);
        _fixture.Api.Verify(
            api => api.FindDeletedAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationKeysRequest>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeletedKey_WithRestoreDeletedAndReplace_RestoresThenReplacesTheRestoredVersion()
    {
        DeletedAmong(Key);
        Restores();

        var exitCode = await Execute(restoreDeleted: true, replace: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe([$"restore:{Key}:False", "get", "replace"]);
    }

    [Fact]
    public async Task Replace_OfAChangedVersion_PrintsTheServerMessage()
    {
        DeletedAmong();
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>(), ETag, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(412, "AnnotationPreconditionFailed", "The annotation changed since it was read."));

        var exitCode = await Execute(replace: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("! The annotation changed since it was read.");
        _fixture.Console.Lines.ShouldContain("Error code: AnnotationPreconditionFailed");
    }

    [Fact]
    public async Task FailingLookup_PrintsTheServerMessage()
    {
        _fixture.Api
            .Setup(api => api.FindDeletedAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationKeysRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidKey", "Too many annotation keys."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe(["! Too many annotation keys.", "Error code: InvalidKey"]);
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletedKey_WithRestoreDeletedAndWithoutTheManagementScope_PrintsTheServerMessageAndWritesNothing()
    {
        DeletedAmong(Key);
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.MissingScope("Annotation.Manage"));

        var exitCode = await Execute(restoreDeleted: true);

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _calls.ShouldBeEmpty();
        _fixture.Console.Lines.ShouldContain($"! {AnnotationCommandFixture.MissingScopeMessage("Annotation.Manage")}");
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

    // Every refusal of the write prints the server's message and code, with the exit code of its status: a rejected
    // annotation (400) and a concurrent change or a deleted annotation (409) are 2, a failure of the service 3.
    [Theory]
    [InlineData(400, "InvalidAnnotation", "Title has 201 characters, at most 200 are allowed.", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "Conflict", "The annotation was changed by another request.", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(500, "Unexpected", "The server failed.", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task RefusedWrite_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, string message, int expected)
    {
        DeletedAmong();
        Patches(AnnotationCommandFixture.Error(status, errorCode, message));

        var exitCode = await Execute();

        exitCode.ShouldBe(expected);
        _fixture.Console.Lines.ShouldBe([$"! {message}", $"Error code: {errorCode}"]);
    }

    [Fact]
    public async Task ConflictOfTheServer_PrintsItsMessage()
    {
        DeletedAmong();
        Patches(AnnotationCommandFixture.Error(409, "Conflict", "The annotations changed concurrently three times in a row."));

        var exitCode = await Execute();

        // A concurrent change is refused input (2) with the server's message, not a crash.
        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("! The annotations changed concurrently three times in a row.");
    }

    private static VersionedResponse<Annotation> Versioned(string? etag = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();

        if (etag is not null)
        {
            headers["ETag"] = [etag];
        }

        return new VersionedResponse<Annotation>(200, AnnotationCommandFixture.Active(Key), headers);
    }

    // The answers of the patches in their order, an exception for a failed one and null for a written one; the last
    // answers every further patch. Without answers, every patch writes.
    private void Patches(params Exception?[] answers)
    {
        var count = 0;

        _fixture.Api
            .Setup(api => api.PatchAnnotationVersionedAsync("org1", "proj1", "default", Key, It.IsAny<JToken>(), null))
            .Callback(() => _calls.Add("patch"))
            .Returns(() =>
            {
                var answer = answers.Length == 0 ? null : answers[Math.Min(count++, answers.Length - 1)];
                return answer is null
                    ? Task.FromResult(Versioned(ETag))
                    : Task.FromException<VersionedResponse<Annotation>>(answer);
            });
    }

    private void CreateFails(int statusCode, string errorCode, string message)
    {
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>(), null, "*"))
            .Callback(() => _calls.Add("create"))
            .ThrowsAsync(AnnotationCommandFixture.Error(statusCode, errorCode, message));
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
            .Callback<string, string, string, AnnotationRestoreRequest, CancellationToken>((_, _, _, request, _) =>
                _calls.Add($"restore:{string.Join(",", request.Keys)}:{request.Descendants}"))
            .ReturnsAsync((string _, string _, string _, AnnotationRestoreRequest request, CancellationToken _) => new AnnotationRestoreResult
            {
                Annotations = request.Keys.Select(AnnotationCommandFixture.Active).ToList(),
                CreatedKeys = [],
                Failed = [],
            });
    }

    private Task<int> Execute(bool restoreDeleted = false, bool recreate = false, bool replace = false)
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
            IsReplaceRequested = replace,
        };

        return command.OnExecuteAsync();
    }
}
