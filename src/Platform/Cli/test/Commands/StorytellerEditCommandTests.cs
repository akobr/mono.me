using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform story edit with a mocked API and a mocked editor: what is read, what the editor gets, and what is written. The
// mocked editor applies the command's check as the real one does: the check's document is the one compared and returned.
public class StorytellerEditCommandTests
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
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new();
    private readonly List<string> _calls = [];
    private readonly Queue<Func<Annotation?>> _reads = new();

    private JObject? _opened;
    private bool? _openedAsNew;
    private string? _fileNamePrefix;
    private Func<JObject, JsonEditCheck>? _check;
    private Annotation? _written;

    public StorytellerEditCommandTests()
    {
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>()))
            .Callback((string _, string _, string _, string _, Annotation body, CancellationToken _) =>
            {
                _calls.Add("set");
                _written = body;
            })
            .ReturnsAsync(AnnotationCommandFixture.Active(Key));

        // Each read takes the next answer; the last one repeats.
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _calls.Add("get");
                var answer = _reads.Count > 1 ? _reads.Dequeue() : _reads.Peek();
                return Task.FromResult(answer()!);
            });
    }

    [Fact]
    public async Task AnExistingKey_IsReadWhateverItsStatus_EditedAndSavedAsAReplace()
    {
        Stored(StoredExecution());
        EditorChanges(document =>
        {
            document["Title"] = null;
            document["Labels"] = new JArray("eu");
            ((JObject)document["Values"]!).Remove("tier");
            document["Description"] = "New description";
        });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "edit", "get", "set"]);
        _openedAsNew.ShouldBe(false);
        _fileNamePrefix.ShouldBe($"annotation-{Key}");
        _opened!["Title"]!.Value<string>().ShouldBe("Invoicing");
        _opened["Labels"]!.Values<string>().ShouldBe(["eu", "prod"]);

        _written.ShouldBeOfType<Execution>();
        _written!.AnnotationKey.ShouldBe(Key);
        _written.Title.ShouldBeNull();
        _written.Description.ShouldBe("New description");
        _written.Labels.ShouldBe(["eu"]);
        _written.Values.Keys.ShouldBe(["plan"]);
        _fixture.Console.Lines.ShouldContain($"! Annotation '{Key}' has been saved.");
        KeptFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMissingKey_SaysSo_AndCreatesItFromAnEmptyDocument()
    {
        Missing();
        DeletedAmong();
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "lookup", "edit", "get", "set"]);
        _openedAsNew.ShouldBe(true);
        _opened!["Title"]!.Type.ShouldBe(JTokenType.Null);
        _written!.Title.ShouldBe("Billing");
        _fixture.Console.Lines.ShouldContain($"Annotation '{Key}' does not exist yet, creating new.");
    }

    [Fact]
    public async Task NoChanges_SaveNothing()
    {
        Stored(StoredExecution());
        EditorAnswers(ExitCodes.WARNING_NO_WORK_NEEDED);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _calls.ShouldBe(["get", "edit"]);
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Nothing was", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoChanges_ToAMissingKey_CreateNothing_AndNameTheCommandThatDoes()
    {
        Missing();
        DeletedAmong();
        EditorAnswers(ExitCodes.WARNING_NO_WORK_NEEDED);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _calls.ShouldBe(["get", "lookup", "edit"]);
        _fixture.Console.Lines.ShouldContain($"Nothing was created. To create it without content: sform story set {Key}");
    }

    [Fact]
    public async Task AChangeOfTheFormOnly_SavesNothing()
    {
        Stored(StoredExecution(labels: [], values: new Dictionary<string, object>()));
        EditorChanges(document =>
        {
            document["Labels"] = null;
            document.Remove("Values");
            document["values"] = null;
            document.Remove("TimeZone");
        });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _calls.ShouldBe(["get", "edit"]);
    }

    [Theory]
    [InlineData(ExitCodes.WARNING_ABORTED)]
    [InlineData(ExitCodes.ERROR_CRASH)]
    public async Task ADeclinedOrFailedEdit_SavesNothing_AndEndsWithTheEditorsCode(int editorCode)
    {
        Stored(StoredExecution());
        EditorAnswers(editorCode);

        var exitCode = await Execute();

        exitCode.ShouldBe(editorCode);
        _calls.ShouldBe(["get", "edit"]);
        KeptFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheEditorChecksTheDocument_AsAnAnnotationOfTheKey_AndGetsItNormalized()
    {
        Stored(StoredExecution());
        EditorAnswers(ExitCodes.WARNING_ABORTED);

        await Execute();

        _check.ShouldNotBeNull();
        JToken.DeepEquals(_check(_opened!).Document, _opened).ShouldBeTrue();
        _check(new JObject { ["Titel"] = "a" }).Problem!.ShouldStartWith("Unknown member 'Titel'.");
        _check(new JObject { ["UnitType"] = "time" }).Problem.ShouldBe("'UnitType' is a member of units only.");
        _check(new JObject { ["Title"] = new string('a', 201) }).Problem.ShouldBe("Title has 201 characters, at most 200 are allowed.");
        _check(new JObject { ["title"] = "Billing", ["Labels"] = new JArray("eu", "eu") }).Document!["Labels"]!.Values<string>().ShouldBe(["eu"]);
    }

    // Sharprompt asks only when neither the input nor the output is redirected.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ARedirectedConsole_EndsBeforeAnyRequest_AndNamesTheWayWithoutTheEditor(bool input, bool output)
    {
        _fixture.InnerConsole.SetupGet(console => console.IsInputRedirected).Returns(input);
        _fixture.InnerConsole.SetupGet(console => console.IsOutputRedirected).Returns(output);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _calls.ShouldBeEmpty();
        _fixture.Line("sform story edit needs an interactive console")
            .ShouldContain($"sform story get {Key} -e <file>, edit the file, then sform story set {Key} -i <file> --replace");
    }

    [Fact]
    public async Task ATypeCodeInAnotherCase_IsReadAndWrittenInLowerCase()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute("EXE.northwind.billing.prod");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "edit", "get", "set"]);
        _fileNamePrefix.ShouldBe($"annotation-{Key}");
        _written!.AnnotationKey.ShouldBe(Key);
    }

    [Fact]
    public async Task ADeletedKey_Cancelled_OpensNoEditor()
    {
        Missing();
        DeletedAmong(Key);
        Answers(StorytellerEditCommand.CancelChoice);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _calls.ShouldBe(["get", "lookup"]);
        _fixture.Line("- exe.northwind.billing.prod").ShouldContain("by Ales Kobr");
        _fixture.Console.Lines.ShouldContain("Nothing was changed.");
    }

    [Fact]
    public async Task ADeletedKey_CreatedNew_EditsAnEmptyDocument_AndWritesWithoutARestore()
    {
        Missing();
        DeletedAmong(Key);
        Answers(StorytellerEditCommand.CreateNewChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "lookup", "edit", "get", "set"]);
        _openedAsNew.ShouldBe(true);
        _opened!["Title"]!.Type.ShouldBe(JTokenType.Null);
    }

    [Fact]
    public async Task ADeletedKey_RestoredAndEdited_EditsItsDeletedRecord_ThenGuardsRestoresAndWrites()
    {
        Missing();
        DeletedAmong(Key);
        DeletedRecord(Key, "Deleted title");
        Restores();
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Restored and renamed");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get", $"restore:{Key}:False", "set"]);
        _openedAsNew.ShouldBe(false);
        _opened!["Title"]!.Value<string>().ShouldBe("Deleted title");
        _written!.Title.ShouldBe("Restored and renamed");
        _fixture.Console.Lines.ShouldContain($"Restored {Key}.");
    }

    [Fact]
    public async Task ADeletedAncestor_RestoredAndEdited_EditsAnEmptyDocument_AndRestoresTheAncestor()
    {
        Missing();
        DeletedAmong("sbt.northwind");
        Restores();
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "lookup", "edit", "get", "restore:sbt.northwind:False", "set"]);
        _openedAsNew.ShouldBe(true);
    }

    // A deleted ancestor takes the key with it, so the lookup lists both, and both are restored.
    [Fact]
    public async Task ADeletedAncestorAndTheKey_RestoredAndEdited_RestoreBoth()
    {
        Missing();
        DeletedAmong("sbt.northwind", Key);
        DeletedRecord(Key, "Deleted title");
        Restores();
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Back");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get", $"restore:sbt.northwind,{Key}:False", "set"]);
        _fixture.Console.Lines.ShouldContain("These annotations are deleted:");
        _opened!["Title"]!.Value<string>().ShouldBe("Deleted title");
    }

    [Fact]
    public async Task ADeletedAncestorAndTheKey_RestoredWithoutChanges_NameTheRestoreOfBoth()
    {
        Missing();
        DeletedAmong("sbt.northwind", Key);
        DeletedRecord(Key, "Deleted title");
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorAnswers(ExitCodes.WARNING_NO_WORK_NEEDED);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Console.Lines.ShouldContain($"Nothing was restored. To restore them as they were: sform story restore sbt.northwind {Key} --without-descendants");
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Nothing was created", StringComparison.Ordinal));
    }

    // The key is missing below a deleted ancestor: nothing restored, and nothing created either.
    [Fact]
    public async Task ADeletedAncestor_RestoredWithoutChanges_NamesBothCommands()
    {
        Missing();
        DeletedAmong("sbt.northwind");
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorAnswers(ExitCodes.WARNING_NO_WORK_NEEDED);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Console.Lines.ShouldContain("Nothing was restored. To restore it as it was: sform story restore sbt.northwind --without-descendants");
        _fixture.Console.Lines.ShouldContain($"Nothing was created. To create it without content: sform story set {Key}");
    }

    // Purged between the lookup and the read of the record: the edit starts from an empty document.
    [Fact]
    public async Task ADeletedRecordGoneBeforeItsRead_IsEditedAsNew()
    {
        Missing();
        DeletedAmong(Key);
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", Key))
            .Callback(() => _calls.Add("deleted"))
            .ThrowsAsync(AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."));
        Restores();
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _openedAsNew.ShouldBe(true);
        _opened!["Title"]!.Type.ShouldBe(JTokenType.Null);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get", $"restore:{Key}:False", "set"]);
    }

    [Fact]
    public async Task ADeletedKey_RestoredWithoutChanges_RestoresNothing_AndNamesTheCommandThatDoes()
    {
        Missing();
        DeletedAmong(Key);
        DeletedRecord(Key, "Deleted title");
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorAnswers(ExitCodes.WARNING_NO_WORK_NEEDED);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit"]);
        _fixture.Console.Lines.ShouldContain($"Nothing was restored. To restore it as it was: sform story restore {Key} --without-descendants");
    }

    [Fact]
    public async Task AFailedRestore_WritesNothing_AndKeepsTheEdit()
    {
        Missing();
        DeletedAmong(Key);
        DeletedRecord(Key, "Deleted title");
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .Callback(() => _calls.Add("restore"))
            .ReturnsAsync(new AnnotationRestoreResult
            {
                Annotations = [],
                CreatedKeys = [],
                Failed = [new AnnotationKeyFailure { Key = Key, Message = "The annotation was purged." }],
            });
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get", "restore"]);
        _fixture.Console.Lines.ShouldContain($"The annotation '{Key}' was not restored: The annotation was purged.");
        KeptEdit()["Title"]!.Value<string>().ShouldBe("Billing");
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("The restored annotations", StringComparison.Ordinal));
    }

    // The server keeps what it restored before a later key failed.
    [Fact]
    public async Task APartlyFailedRestore_NamesWhatWasRestored_AndKeepsTheEdit()
    {
        Missing();
        DeletedAmong("sbt.northwind", Key);
        DeletedRecord(Key, "Deleted title");
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .Callback(() => _calls.Add("restore"))
            .ReturnsAsync(new AnnotationRestoreResult
            {
                Annotations = [AnnotationCommandFixture.Active("sbt.northwind")],
                CreatedKeys = [],
                Failed = [new AnnotationKeyFailure { Key = Key, Message = "The annotation was purged." }],
            });
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldNotContain("set");
        _fixture.Console.Lines.ShouldContain("Restored sbt.northwind.");
        _fixture.Console.Lines.ShouldContain($"The annotation '{Key}' was not restored: The annotation was purged.");
        _fixture.Console.Lines.ShouldContain("The restored annotations keep the content they had when they were deleted.");
        KeptFiles().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARestoreWithoutTheManagementScope_PrintsTheServerMessage_WritesNothing_AndKeepsTheEdit()
    {
        Missing();
        DeletedAmong(Key);
        DeletedRecord(Key, "Deleted title");
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.MissingScope("Annotation.Manage"));
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get"]);
        _fixture.Console.Lines.ShouldContain($"! {AnnotationCommandFixture.MissingScopeMessage("Annotation.Manage")}");
        KeptFiles().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARestoreFollowedByAFailedWrite_SaysWhatStaysRestored_AndKeepsTheEdit()
    {
        Missing();
        DeletedAmong(Key);
        DeletedRecord(Key, "Deleted title");
        Restores();
        Answers(StorytellerEditCommand.RestoreAndEditChoice);
        EditorChanges(document => document["DocumentationLink"] = "https://example.com/runbook");
        WriteFails(AnnotationCommandFixture.Error(400, "InvalidAnnotation", "The annotation is not valid."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBe(["get", "lookup", "deleted", "edit", "get", $"restore:{Key}:False"]);
        _fixture.Console.Lines.ShouldContain("The annotation is not valid.");
        _fixture.Console.Lines.ShouldContain("The restored annotations keep the content they had when they were deleted.");
        KeptEdit()["DocumentationLink"]!.Value<string>().ShouldBe("https://example.com/runbook");
    }

    [Fact]
    public async Task AChangeWhileTheEditorWasOpen_SavesNothing_AndKeepsTheEdit()
    {
        Stored(StoredExecution());
        _reads.Enqueue(() =>
        {
            var changed = StoredExecution();
            changed.Title = "Changed by someone else";
            return changed;
        });
        EditorChanges(document => document["Description"] = "Mine");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBe(["get", "edit", "get"]);
        _fixture.Console.Lines.ShouldContain($"! The annotation '{Key}' changed while you edited it, so your edit was not saved.");
        var kept = KeptFiles().ShouldHaveSingleItem();
        KeptEdit()["Description"]!.Value<string>().ShouldBe("Mine");
        _fixture.Console.Lines.ShouldContain($"Your edit is in {kept}. To save it anyway: sform story set {Key} -i \"{kept}\" --replace");
    }

    [Fact]
    public async Task AKeyDeletedWhileTheEditorWasOpen_SavesNothing_AndKeepsTheEdit()
    {
        Stored(StoredExecution());
        Missing();
        EditorChanges(document => document["Title"] = "Mine");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBe(["get", "edit", "get"]);
        KeptEdit()["Title"]!.Value<string>().ShouldBe("Mine");
    }

    [Fact]
    public async Task AKeyCreatedWhileTheEditorWasOpen_SavesNothing_AndKeepsTheEdit()
    {
        Missing();
        _reads.Enqueue(() => StoredExecution());
        DeletedAmong();
        EditorChanges(document => document["Title"] = "Mine");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _calls.ShouldBe(["get", "lookup", "edit", "get"]);
        KeptEdit()["Title"]!.Value<string>().ShouldBe("Mine");
    }

    [Fact]
    public async Task AChangeOfTheRelationsOnly_IsNoChangeOfTheContent()
    {
        Stored(StoredExecution());
        _reads.Enqueue(() =>
        {
            var changed = StoredExecution();
            changed.UnitNames = ["export"];
            changed.Modified = new AuditStamp { At = DateTimeOffset.UtcNow };
            return changed;
        });
        EditorChanges(document => document["Title"] = "Billing");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
    }

    [Theory]
    [InlineData(400, "InvalidAnnotation", "Title has 201 characters, at most 200 are allowed.", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "Conflict", "The annotation changed concurrently three times in a row.", ExitCodes.ERROR_CRASH)]
    public async Task ARejectedWrite_PrintsTheServerMessage_AndKeepsTheEdit(int status, string errorCode, string message, int expected)
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        WriteFails(AnnotationCommandFixture.Error(status, errorCode, message));

        var exitCode = await Execute();

        exitCode.ShouldBe(expected);
        _fixture.Console.Lines.ShouldContain(message);
        KeptEdit()["Title"]!.Value<string>().ShouldBe("Billing");
    }

    [Fact]
    public async Task AWriteWithoutTheScope_PrintsTheServerMessage_AndKeepsTheEdit()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        WriteFails(AnnotationCommandFixture.MissingScope("Annotation.Write"));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldContain($"! {AnnotationCommandFixture.MissingScopeMessage("Annotation.Write")}");
        _fixture.Console.Lines.ShouldContain("Error code: AccessDenied");
        KeptFiles().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnotherFailedWrite_IsACrash_AndKeepsTheEdit()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        WriteFails(AnnotationCommandFixture.Error(500));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _fixture.Console.Lines.ShouldContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
        KeptFiles().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnUnauthorizedOrUnreachableWrite_IsLeftToTheTopLevel_AfterTheEditIsKept()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>()))
            .ThrowsAsync(new HttpRequestException("No such host is known."));

        await Should.ThrowAsync<HttpRequestException>(() => Execute());

        KeptEdit()["Title"]!.Value<string>().ShouldBe("Billing");
    }

    [Fact]
    public async Task AnUnauthorizedWrite_IsLeftToTheTopLevel_AfterTheEditIsKept()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        WriteFails(AnnotationCommandFixture.Error(401));

        (await Should.ThrowAsync<ApiException>(() => Execute())).StatusCode.ShouldBe(401);

        KeptEdit()["Title"]!.Value<string>().ShouldBe("Billing");
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }

    // A failure to keep the edit does not replace the outcome of the write.
    [Fact]
    public async Task AnEditThatCannotBeKept_IsSaid_AndTheWritesOutcomeStays()
    {
        Stored(StoredExecution());
        EditorChanges(document => document["Title"] = "Billing");
        WriteFails(AnnotationCommandFixture.Error(400, "InvalidAnnotation", "The annotation is not valid."));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.Path.GetTempPath()).Returns("/tmp/");
        fileSystem.Setup(system => system.Path.GetRandomFileName()).Returns("abcdefgh.tmp");
        fileSystem.Setup(system => system.Path.Combine(It.IsAny<string>(), It.IsAny<string>())).Returns((string first, string second) => first + second);
        fileSystem
            .Setup(system => system.File.WriteAllText(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Encoding>()))
            .Throws(new IOException("The disk is full."));

        var exitCode = await Execute(Key, fileSystem.Object);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("The annotation is not valid.");
        _fixture.Console.Lines.ShouldContain("! Your edit could not be kept in a file: The disk is full.");
    }

    [Fact]
    public async Task ARefusedRead_OpensNoEditor()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "AnnotationRestricted", "The machine may not read it."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldContain("! The machine may not read it.");
        _editor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(403, ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(500, ExitCodes.ERROR_CRASH)]
    public async Task AFailedLookup_OpensNoEditor(int status, int expected)
    {
        Missing();
        _fixture.Api
            .Setup(api => api.FindDeletedAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationKeysRequest>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, "Failed", "The lookup failed."));

        var exitCode = await Execute();

        exitCode.ShouldBe(expected);
        _editor.VerifyNoOtherCalls();
        KeptFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnauthorizedRead_IsLeftToTheTopLevel()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        (await Should.ThrowAsync<ApiException>(() => Execute())).StatusCode.ShouldBe(401);
        KeptFiles().ShouldBeEmpty();
    }

    private static Execution StoredExecution(ICollection<string>? labels = null, IDictionary<string, object>? values = null)
    {
        return new Execution
        {
            ProjectName = AnnotationCommandFixture.Project,
            ViewName = AnnotationCommandFixture.View,
            AnnotationKey = Key,
            Name = "prod",
            Title = "Invoicing",
            Labels = labels ?? ["eu", "prod"],
            Values = values ?? new Dictionary<string, object> { ["tier"] = "gold", ["plan"] = "pro" },
        };
    }

    private void Stored(Annotation annotation)
    {
        _reads.Enqueue(() => annotation);
    }

    private void Missing()
    {
        _reads.Enqueue(() => throw AnnotationCommandFixture.Error(404, "AnnotationNotFound", "The annotation does not exist."));
    }

    private void DeletedAmong(params string[] deletedKeys)
    {
        _fixture.Api
            .Setup(api => api.FindDeletedAnnotationsAsync(
                "org1",
                "proj1",
                "default",
                It.Is<AnnotationKeysRequest>(request => request.Keys.OrderBy(key => key).SequenceEqual(LookedUp.OrderBy(key => key)))))
            .Callback(() => _calls.Add("lookup"))
            .ReturnsAsync(deletedKeys.Select(key => AnnotationCommandFixture.Deleted(key)).ToList());
    }

    // The read of one deleted record, which has the members of its type.
    private void DeletedRecord(string key, string title)
    {
        var record = AnnotationCommandFixture.Deleted(key);
        record.Title = title;

        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", key))
            .Callback(() => _calls.Add("deleted"))
            .ReturnsAsync(record);
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

    private void Answers(string choice)
    {
        _fixture.Console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns(choice);
    }

    private void WriteFails(Exception exception)
    {
        _fixture.Api
            .Setup(api => api.SetAnnotationAsync("org1", "proj1", "default", Key, It.IsAny<Annotation>()))
            .ThrowsAsync(exception);
    }

    // The editor returns a copy of the document it got, changed, as the check gives it back; nothing to save when the
    // check's document equals the original, as EditorService does.
    private void EditorChanges(Action<JObject> change)
    {
        SetupEditor((original, check) =>
        {
            var edited = (JObject)original.DeepClone();
            change(edited);

            var checkedDocument = check!(edited).Document.ShouldNotBeNull();
            return JToken.DeepEquals(original, checkedDocument)
                ? new JsonEditResult(null, ExitCodes.WARNING_NO_WORK_NEEDED)
                : new JsonEditResult(checkedDocument, ExitCodes.SUCCESS);
        });
    }

    // The editor returns no document, with its exit code.
    private void EditorAnswers(int exitCode)
    {
        SetupEditor((_, _) => new JsonEditResult(null, exitCode));
    }

    private void SetupEditor(Func<JObject, Func<JObject, JsonEditCheck>?, JsonEditResult> result)
    {
        _editor
            .Setup(editor => editor.EditJsonAsync(
                It.IsAny<IExtendedConsole>(),
                It.IsAny<EditorOptions>(),
                It.IsAny<JObject>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                true,
                It.IsAny<Func<JObject, JsonEditCheck>?>()))
            .ReturnsAsync((IExtendedConsole _, EditorOptions _, JObject original, string prefix, bool isNew, bool _, Func<JObject, JsonEditCheck>? check) =>
            {
                _calls.Add("edit");
                _opened = (JObject)original.DeepClone();
                _fileNamePrefix = prefix;
                _openedAsNew = isNew;
                _check = check;
                return result(original, check);
            });
    }

    private List<string> KeptFiles()
    {
        return _fileSystem.AllFiles.Where(path => path.Contains($"annotation-{Key}", StringComparison.Ordinal)).ToList();
    }

    private JObject KeptEdit()
    {
        return JObject.Parse(_fileSystem.File.ReadAllText(KeptFiles().ShouldHaveSingleItem()));
    }

    private Task<int> Execute()
    {
        return Execute(Key);
    }

    private Task<int> Execute(string key, IFileSystem? fileSystem = null)
    {
        var command = new StorytellerEditCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            _fixture.Api.Object,
            _editor.Object,
            Options.Create(new EditorOptions()),
            fileSystem ?? _fileSystem)
        {
            AnnotationKey = key,
        };

        return command.OnExecuteAsync();
    }
}
