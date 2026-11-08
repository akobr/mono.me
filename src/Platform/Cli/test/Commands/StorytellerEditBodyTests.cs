using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Cli.UnitTests.Services;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using CoreUnit = _42.Platform.Storyteller.Unit;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform story edit through the real generated client, which reads the stored annotation and serializes the write as in
// production: the mocked client of StorytellerEditCommandTests does neither. The end-to-end cases also run the real
// EditorService with a stand-in for the editor.
public class StorytellerEditBodyTests
{
    private const string ExecutionKey = "exe.northwind.billing.prod";
    private const string UnitKey = "unt.billing.export";
    private const string NotFound = """{ "Message": "The annotation does not exist.", "ErrorCode": "AnnotationNotFound" }""";
    private const string PreconditionFailed = """{ "Message": "The annotation changed since it was read.", "ErrorCode": "AnnotationPreconditionFailed" }""";
    private const string ETag = "\"a1.dmVyc2lvbg\"";

    // As a read answers them: date-like strings unparsed, the relation arrays and the stamps included.
    private const string StoredExecution = """
        {
          "AnnotationType": "Execution", "ProjectName": "proj1", "ViewName": "default",
          "AnnotationKey": "exe.northwind.billing.prod", "Name": "prod",
          "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing",
          "ContextKey": "cnt.northwind.prod", "ContextName": "prod", "UsageKey": "usg.northwind.billing",
          "Title": "Stored", "Description": "Stored description", "DocumentationLink": "https://example.com/stored",
          "TimeZone": "Europe/Prague", "ValidFrom": "2026-01-01T00:00:00+05:30", "IsDisabled": true,
          "Labels": ["stored", "eu"],
          "Values": { "tier": "gold", "since": "2026-10-24T10:00:00.000+02:00", "nested": { "at": "2026-10-24T10:00:00Z" } },
          "UnitNames": ["export"],
          "Created": { "At": "2026-01-01T00:00:00Z" }, "Modified": { "At": "2026-02-01T00:00:00Z" }
        }
        """;

    private const string StoredUnit = """
        {
          "AnnotationType": "Unit", "ProjectName": "proj1", "ViewName": "default",
          "AnnotationKey": "unt.billing.export", "Name": "export", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing",
          "UnitType": "time", "UnitDefinition": "0 2 * * *"
        }
        """;

    private const string Deletion = """{ "DeletionId": "d1", "RootKey": "unt.billing.export", "DeletedAt": "2026-10-11T12:00:00Z", "IsRoot": true }""";

    // What the API reads a body with (AnnotationsHttp.AnnotationBodyOptions).
    private static readonly JsonSerializerOptions ApiOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly ScriptedHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);

    public StorytellerEditBodyTests()
    {
        _fixture.Console.Mock.Setup(console => console.Confirm(It.IsAny<ConfirmOptions>())).Returns(true);
    }

    [Theory]
    [InlineData("rst.billing")]
    [InlineData("sbt.northwind")]
    [InlineData(UnitKey)]
    [InlineData("usg.northwind.billing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData(ExecutionKey)]
    [InlineData("uxe.northwind.billing.prod.export")]
    public async Task TheBodyOfANewKey_IsWhatTheApiReads(string key)
    {
        var body = await EditNewAsync(key, document => document["Title"] = "New");

        var type = AnnotationKey.Parse(key).Type;
        var annotation = (CoreAnnotation)JsonSerializer.Deserialize(body.ToString(), AnnotationTypes.GetRuntimeType(type), ApiOptions)!;

        annotation.AnnotationKey.ShouldBe(key);
        annotation.AnnotationType.ShouldBe(type);
        annotation.Name.ShouldBe(key.Split('.').Last());
        annotation.ProjectName.ShouldBe(AnnotationCommandFixture.Project);
        annotation.ViewName.ShouldBe(AnnotationCommandFixture.View);
        annotation.Title.ShouldBe("New");
    }

    [Fact]
    public async Task AReplace_LeavesOutWhatTheDocumentNoLongerHolds()
    {
        var body = await EditStoredAsync(ExecutionKey, StoredExecution, document =>
        {
            document["Title"] = null;
            document.Remove("Description");
            document["Labels"] = new JArray("eu");
            ((JObject)document["Values"]!).Remove("tier");
            document["IsDisabled"] = false;
        });

        body.ContainsKey("Title").ShouldBeFalse();
        body.ContainsKey("Description").ShouldBeFalse();
        body["DocumentationLink"]!.Value<string>().ShouldBe("https://example.com/stored");
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
        ((JObject)body["Values"]!).Properties().Select(property => property.Name).ShouldBe(["since", "nested"]);
        body["IsDisabled"]!.Value<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task TheBody_LeavesTheRelationsAndTheStampsToTheServer_AndHasTheMembersTheKeyDecides()
    {
        var body = await EditStoredAsync(ExecutionKey, StoredExecution, document => document["Title"] = "Changed");

        body.ContainsKey("UnitNames").ShouldBeFalse();
        body.ContainsKey("Created").ShouldBeFalse();
        body.ContainsKey("Modified").ShouldBeFalse();
        body["AnnotationKey"]!.Value<string>().ShouldBe(ExecutionKey);
        body["SubjectKey"]!.Value<string>().ShouldBe("sbt.northwind");
        body["UsageKey"]!.Value<string>().ShouldBe("usg.northwind.billing");
    }

    // A read keeps these strings as stored (ApiSdk.partial.cs), so an edit of another member must write the same text.
    [Fact]
    public async Task DateLikeStrings_AndTheValidityWindow_StayAsStored()
    {
        var body = await EditStoredAsync(ExecutionKey, StoredExecution, document => document["Title"] = "Changed");

        body["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        body["Values"]!["nested"]!["at"]!.Value<string>().ShouldBe("2026-10-24T10:00:00Z");
        DateTimeOffset.Parse(body["ValidFrom"]!.Value<string>()!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(5.5)));
        body["ValidFrom"]!.Value<string>().ShouldEndWith("+05:30");
    }

    [Fact]
    public async Task TheEditorGetsTheStoredContent_WithItsDatesAsStored()
    {
        JObject? opened = null;
        await EditStoredAsync(ExecutionKey, StoredExecution, document => document["Title"] = "Changed", original => opened = original);

        opened!["ValidFrom"]!.Value<string>().ShouldBe("2026-01-01T00:00:00+05:30");
        opened["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        opened["Labels"]!.Values<string>().ShouldBe(["stored", "eu"]);
    }

    [Fact]
    public async Task AUnit_KeepsItsTypeAndDefinition()
    {
        var body = await EditStoredAsync(UnitKey, StoredUnit, document => document["Title"] = "Export");

        body["UnitType"]!.Value<string>().ShouldBe("time");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
        body["ResponsibilityKey"]!.Value<string>().ShouldBe("rst.billing");
    }

    [Fact]
    public async Task AUnitWithoutItsTypeOrDefinition_GetsTheDefaultsOfACreate()
    {
        var body = await EditStoredAsync(UnitKey, StoredUnit, document =>
        {
            document.Remove("UnitType");
            document["UnitDefinition"] = null;
        });

        body["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        body["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public async Task TheRequests_AreTheRead_ThenTheWriteOfThatVersionToTheRouteOfTheKey()
    {
        await EditStoredAsync(ExecutionKey, StoredExecution, document => document["Title"] = "Changed");

        _http.Requests.Count.ShouldBe(2);
        _http.Requests[0].Method.ShouldBe(HttpMethod.Get);
        _http.Requests[0].Uri.Query.ShouldContain("includeInactive=true");
        _http.Requests[1].Method.ShouldBe(HttpMethod.Post);
        _http.Requests[1].Headers!["If-Match"].ShouldBe(ETag);
        _http.Requests[1].Headers!.ContainsKey("If-None-Match").ShouldBeFalse();
        _http.Requests[1].Uri.AbsolutePath.ShouldBe($"/api/v1/{AnnotationCommandFixture.Organization}/{AnnotationCommandFixture.Project}/{AnnotationCommandFixture.View}/annotations/{ExecutionKey}");
    }

    [Fact]
    public async Task ANewKey_IsLookedUpAmongTheDeleted_BeforeTheEditor()
    {
        await EditNewAsync(ExecutionKey, document => document["Title"] = "New");

        _http.Requests.Count.ShouldBe(3);
        _http.Requests[1].Method.ShouldBe(HttpMethod.Post);
        _http.Requests[1].Uri.AbsolutePath.ShouldEndWith("/annotations/deleted/lookup");
    }

    // A new key is created only while it is still missing.
    [Fact]
    public async Task ANewKey_IsWrittenWithIfNoneMatchAny()
    {
        await EditNewAsync(ExecutionKey, document => document["Title"] = "New");

        var write = _http.Requests[^1];
        write.Method.ShouldBe(HttpMethod.Post);
        write.Headers!["If-None-Match"].ShouldBe("*");
        write.Headers!.ContainsKey("If-Match").ShouldBeFalse();
    }

    // Someone else changed the annotation meanwhile: the write answers 412, the edit is kept and nothing is written again.
    [Fact]
    public async Task AChangeWhileTheEditorWasOpen_AnswersPreconditionFailed_AndKeepsTheEdit()
    {
        _http.Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag));
        _http.Respond(HttpStatusCode.PreconditionFailed, PreconditionFailed);
        _http.Respond(HttpStatusCode.OK, StoredExecution.Replace("\"Stored\"", "\"Changed by someone else\"", StringComparison.Ordinal), ("ETag", "\"a1.b3RoZXI\""));
        SetupEditor(document => document["Description"] = "Mine", null);

        var exitCode = await Command(ExecutionKey, Client(), _editor.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _http.Requests.Count.ShouldBe(3);
        _http.Requests[1].Headers!["If-Match"].ShouldBe(ETag);
        _http.Requests[2].Method.ShouldBe(HttpMethod.Get);
        _fixture.Console.Lines.ShouldContain($"! The annotation '{ExecutionKey}' changed while you edited it, so your edit was not saved.");
        _fileSystem.AllFiles.ShouldHaveSingleItem().ShouldContain($"annotation-{ExecutionKey}");
    }

    // Only a relation array changed meanwhile (a child was added): the edit is written again over the new version.
    [Fact]
    public async Task AChangeOfTheRelationsOnly_WritesAgainWithTheNewETag()
    {
        _http.Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag));
        _http.Respond(HttpStatusCode.PreconditionFailed, PreconditionFailed);
        _http.Respond(HttpStatusCode.OK, StoredExecution.Replace("\"UnitNames\": [\"export\"]", "\"UnitNames\": [\"export\", \"import\"]", StringComparison.Ordinal), ("ETag", "\"a1.bmV3\""));
        _http.Respond(HttpStatusCode.OK, Answer(ExecutionKey));

        var body = await EditAsync(ExecutionKey, document => document["Title"] = "Changed", null);

        body["Title"]!.Value<string>().ShouldBe("Changed");
        _http.Requests.Count.ShouldBe(4);
        _http.Requests[3].Headers!["If-Match"].ShouldBe("\"a1.bmV3\"");
    }

    // The lookup answers the base members only, as the API writes its list; the read of one deleted record has the rest.
    [Fact]
    public async Task ADeletedUnit_RestoredAndEdited_KeepsItsTypeAndDefinition()
    {
        JObject? opened = null;
        _fixture.Console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns(StorytellerEditCommand.RestoreAndEditChoice);
        _http.Respond(HttpStatusCode.NotFound, NotFound);
        _http.Respond(HttpStatusCode.OK, $$"""[{ "AnnotationType": "Unit", "ProjectName": "proj1", "ViewName": "default", "AnnotationKey": "{{UnitKey}}", "Name": "export", "Title": "Deleted", "Deletion": {{Deletion}} }]""");
        _http.Respond(HttpStatusCode.OK, StoredUnit.Replace("\"UnitType\"", $"\"Title\": \"Deleted\", \"Deletion\": {Deletion}, \"UnitType\"", StringComparison.Ordinal));
        _http.Respond(HttpStatusCode.NotFound, NotFound);
        _http.Respond(HttpStatusCode.OK, $$"""{ "Annotations": [{{Answer(UnitKey)}}], "CreatedKeys": [], "Failed": [] }""");
        _http.Respond(HttpStatusCode.OK, Answer(UnitKey));

        var body = await EditAsync(UnitKey, document => document["Title"] = "Restored", original => opened = original);

        opened!["Title"]!.Value<string>().ShouldBe("Deleted");
        opened["UnitType"]!.Value<string>().ShouldBe("time");
        opened["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
        body["UnitType"]!.Value<string>().ShouldBe("time");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
        _http.Requests[2].Uri.AbsolutePath.ShouldEndWith($"/annotations/deleted/{UnitKey}");
        _http.Requests[4].Uri.AbsolutePath.ShouldEndWith("/annotations/deleted/restore");
    }

    // As before the fix of sform story set, when the SDK refused a body: a plain error line and exit code, not a crash.
    [Fact]
    public async Task ABodyTheClientCannotWrite_IsAnErrorLine_AndKeepsTheEdit()
    {
        using var error = new StringWriter();
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        _http.Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag));
        SetupEditor(document => document["Title"] = "Changed", null);

        var exitCode = await Command(ExecutionKey, new UnwritableBodyClient(new HttpClient(_http)), _editor.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        error.ToString().ShouldContain($"The annotation {ExecutionKey} could not be sent");
        _fileSystem.AllFiles.ShouldHaveSingleItem().ShouldContain($"annotation-{ExecutionKey}");
    }

    [Fact]
    public async Task EndToEnd_AnEditInTheEditor_IsSaved()
    {
        _http.Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag));
        _http.Respond(HttpStatusCode.OK, Answer(ExecutionKey));
        string edit = """
            {
              // only the title and the labels are left
              "title": "Edited",
              "Labels": ["eu", "eu"],
            }
            """;
        var editor = new ScriptedEditor(_fileSystem, [edit]);

        var exitCode = await Command(ExecutionKey, Client(), editor).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var body = Sent();
        body["Title"]!.Value<string>().ShouldBe("Edited");
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
        body.ContainsKey("Description").ShouldBeFalse();
        body.ContainsKey("ValidFrom").ShouldBeFalse();
        ((JObject)body["Values"]!).Count.ShouldBe(0);
        _fileSystem.AllFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task EndToEnd_AChangeOfTheFormOnly_SavesNothing_AndAsksNothing()
    {
        _http.Respond(HttpStatusCode.OK, StoredUnit);
        var editor = new ScriptedEditor(_fileSystem, ["""{ "title": null, "Labels": null, "Values": null, "UnitType": "time", "UnitDefinition": "0 2 * * *" }"""]);

        var exitCode = await Command(UnitKey, Client(), editor).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _http.Requests.Count.ShouldBe(1);
        _fixture.Console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
        _fixture.Console.Lines.ShouldContain("No changes detected.");
    }

    [Fact]
    public async Task EndToEnd_ADocumentThatBreaksARule_ReopensTheEditor()
    {
        _http.Respond(HttpStatusCode.OK, StoredUnit, ("ETag", ETag));
        _http.Respond(HttpStatusCode.OK, Answer(UnitKey));
        var editor = new ScriptedEditor(_fileSystem, [
            """{ "DocumentationLink": "wiki.example.com/export", "UnitType": "time", "UnitDefinition": "0 2 * * *" }""",
            """{ "DocumentationLink": "https://wiki.example.com/export", "UnitType": "time", "UnitDefinition": "0 2 * * *" }""",
        ]);

        var exitCode = await Command(UnitKey, Client(), editor).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        editor.Opened.Count.ShouldBe(2);
        _fixture.Console.Lines.ShouldContain("! DocumentationLink must be an absolute http or https URL.");
        Sent()["DocumentationLink"]!.Value<string>().ShouldBe("https://wiki.example.com/export");
    }

    // The read of the stored annotation with its ETag, then the write of that version.
    private Task<JObject> EditStoredAsync(string key, string stored, Action<JObject> change, Action<JObject>? opened = null)
    {
        _http.Respond(HttpStatusCode.OK, stored, ("ETag", ETag));
        _http.Respond(HttpStatusCode.OK, Answer(key));
        return EditAsync(key, change, opened);
    }

    // The read (404), the lookup of deleted keys (none), and the create (If-None-Match: *).
    private Task<JObject> EditNewAsync(string key, Action<JObject> change)
    {
        _http.Respond(HttpStatusCode.NotFound, NotFound);
        _http.Respond(HttpStatusCode.OK, "[]");
        _http.Respond(HttpStatusCode.OK, Answer(key));
        return EditAsync(key, change, null);
    }

    private async Task<JObject> EditAsync(string key, Action<JObject> change, Action<JObject>? opened)
    {
        SetupEditor(change, opened);

        (await Command(key, Client(), _editor.Object).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        return Sent();
    }

    // The editor returns the document it got, as its file is read back, changed and given to the check.
    private void SetupEditor(Action<JObject> change, Action<JObject>? opened)
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
            .ReturnsAsync((IExtendedConsole _, EditorOptions _, JObject original, string _, bool _, bool _, Func<JObject, JsonEditCheck>? check) =>
            {
                opened?.Invoke(original);

                var edited = EditorService.ParseDocument(original.ToString(Formatting.Indented));
                change(edited);
                return new JsonEditResult(check!(edited).Document.ShouldNotBeNull(), ExitCodes.SUCCESS);
            });
    }

    // Without parsing dates: the assertions are about the text that was sent.
    private JObject Sent()
    {
        using var reader = new JsonTextReader(new StringReader(_http.Requests[^1].Body!)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    private AnnotationsApiClient Client()
    {
        return new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = "http://localhost:7071/api" });
    }

    private StorytellerEditCommand Command(string key, IAnnotationsApiClient client, IEditorService editor)
    {
        return new StorytellerEditCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            client,
            editor,
            Options.Create(ScriptedEditor.Vim),
            _fileSystem)
        {
            AnnotationKey = key,
        };
    }

    private static string Answer(string key)
    {
        return new JObject
        {
            ["AnnotationType"] = AnnotationKey.Parse(key).Type.ToString(),
            ["ProjectName"] = AnnotationCommandFixture.Project,
            ["ViewName"] = AnnotationCommandFixture.View,
            ["AnnotationKey"] = key,
            ["Name"] = key.Split('.').Last(),
        }.ToString();
    }

    // The SDK's refusal of a body, as it threw for a missing required member.
    private sealed class UnwritableBodyClient(HttpClient httpClient)
        : AnnotationsApiClient(httpClient, new SdkConfiguration { BaseUrl = "http://localhost:7071/api" })
    {
        public override Task<_42.Platform.Storyteller.Sdk.Annotation> SetAnnotationAsync(string organization, string project, string view, string key, _42.Platform.Storyteller.Sdk.Annotation body, string if_Match = null!, string if_None_Match = null!, CancellationToken cancellationToken = default)
        {
            throw new JsonSerializationException("Cannot write a null value for property 'AnnotationKey'. Property requires a value. Path ''.");
        }
    }
}
