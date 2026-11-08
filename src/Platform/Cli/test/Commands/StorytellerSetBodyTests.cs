using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using CoreUnit = _42.Platform.Storyteller.Unit;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using SdkAnnotation = _42.Platform.Storyteller.Sdk.Annotation;

namespace _42.Platform.Cli.UnitTests.Commands;

// The requests of sform story set through the real generated client, which writes them as in production: the mocked
// client of StorytellerSetCommandTests never does, and the SDK's required members are checked only here. A merge is one
// JSON Patch; a missing key is created by a POST that holds the input alone.
public class StorytellerSetBodyTests
{
    private const string Key = "exe.northwind.billing.prod";
    private const string UnitKey = "unt.billing.export";
    private const string ImportFile = "annotation.json";
    private const string ETag = "\"a1.djE\"";
    private const string JsonPatchMediaType = "application/json-patch+json";
    private const string NotFound = """{ "Message": "The annotation does not exist.", "ErrorCode": "AnnotationNotFound" }""";
    private const string Deleted = """{ "Message": "The annotation is deleted.", "ErrorCode": "AnnotationDeleted" }""";
    private const string Exists = """{ "Message": "The annotation exists already; read it again.", "ErrorCode": "AnnotationPreconditionFailed" }""";
    private const string Changed = """{ "Message": "The annotation changed since it was read; read it again.", "ErrorCode": "AnnotationPreconditionFailed" }""";

    // As a read answers them: date-like strings unparsed, the relation arrays included.
    private const string StoredExecution = """
        {
          "AnnotationType": "Execution", "ProjectName": "proj1", "ViewName": "default",
          "AnnotationKey": "exe.northwind.billing.prod", "Name": "prod",
          "Title": "Stored", "Description": "Stored description", "DocumentationLink": "https://example.com/stored",
          "TimeZone": "Europe/Prague", "ValidFrom": "2026-01-01T00:00:00+00:00", "IsDisabled": true,
          "Labels": ["stored"],
          "Values": { "tier": "gold", "since": "2026-10-24T10:00:00.000+02:00", "nested": { "at": 1 } },
          "UnitNames": ["export"]
        }
        """;

    // Date-like strings in a member and in values, and a typed member with an offset.
    private const string DateLikeFile = """
        {
          "Title": "2026-10-24T10:00:00Z",
          "ValidFrom": "2026-10-24T10:00:00+05:30",
          "Values": {
            "since": "2026-10-24T10:00:00.000+02:00",
            "nested": { "at": "2026-10-24T10:00:00+05:30" },
            "list": [ "2026-10-24T10:00:00.1234567Z" ]
          }
        }
        """;

    // What the API reads a body with (AnnotationsHttp.AnnotationBodyOptions): into the runtime type of its AnnotationType,
    // where a missing required member is a 400.
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

    [Fact]
    public async Task Merge_IsOneJsonPatch_WithoutARead()
    {
        var patch = await PatchAsync(Key, configure: command => command.Labels = ["eu"]);

        var request = _http.Requests[1];
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Uri.AbsolutePath.ShouldBe($"/api/v1/{AnnotationCommandFixture.Organization}/{AnnotationCommandFixture.Project}/{AnnotationCommandFixture.View}/annotations/{Key}");
        request.Headers!["Content-Type"].ShouldBe(JsonPatchMediaType);
        request.Headers.ShouldNotContainKey("If-Match");
        request.Headers.ShouldNotContainKey("If-None-Match");
        _http.Requests.ShouldNotContain(sent => sent.Method == HttpMethod.Get);
        patch.ShouldAllBe(operation => operation["op"]!.Value<string>() == "add");
    }

    [Fact]
    public async Task Merge_WithoutInput_IsAnEmptyPatch()
    {
        var patch = await PatchAsync(Key);

        patch.ShouldBeEmpty();
    }

    [Fact]
    public async Task Patch_SetsEveryEditableMemberOfTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""
            {
              "Title": "Billing",
              "Description": "What it does",
              "DocumentationLink": "https://example.com/runbook",
              "TimeZone": "Europe/Prague",
              "ValidFrom": "2026-10-01T00:00:00+00:00",
              "ExpiresAt": "2027-10-01T00:00:00+00:00",
              "IsDisabled": true,
              "Labels": ["eu"],
              "Values": { "tier": "gold" }
            }
            """));

        var patch = await PatchAsync(Key, ImportFile);

        Paths(patch).ShouldBe(["/IsDisabled", "/ValidFrom", "/ExpiresAt", "/TimeZone", "/Title", "/Description", "/DocumentationLink", "/Values/tier", "/Labels/-"]);
        Value(patch, "/IsDisabled").Value<bool>().ShouldBeTrue();
        Value(patch, "/ValidFrom").Value<string>().ShouldStartWith("2026-10-01T00:00:00");
        Value(patch, "/ExpiresAt").Value<string>().ShouldStartWith("2027-10-01T00:00:00");
        Value(patch, "/TimeZone").Value<string>().ShouldBe("Europe/Prague");
        Value(patch, "/Title").Value<string>().ShouldBe("Billing");
        Value(patch, "/Description").Value<string>().ShouldBe("What it does");
        Value(patch, "/DocumentationLink").Value<string>().ShouldBe("https://example.com/runbook");
        Value(patch, "/Values/tier").Value<string>().ShouldBe("gold");
        Labels(patch).ShouldBe(["eu"]);
    }

    // The server keeps what the patch does not name: the other members, values and labels.
    [Fact]
    public async Task Patch_NamesOnlyWhatTheInputHolds()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Billing", "IsDisabled": false, "Description": null }"""));

        var patch = await PatchAsync(Key, ImportFile);

        Paths(patch).ShouldBe(["/IsDisabled", "/Title"]);
        Value(patch, "/IsDisabled").Type.ShouldBe(JTokenType.Boolean);
        Value(patch, "/IsDisabled").Value<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Patch_HasTheContentOfTheFileAndTheOptions()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Billing", "Labels": ["eu", "prod"], "Values": { "tier": "gold" } }"""));

        var patch = await PatchAsync(Key, ImportFile, command =>
        {
            command.CustomProperties = ["plan=pro"];
            command.Labels = ["prod", "ops"];
        });

        Value(patch, "/Title").Value<string>().ShouldBe("Billing");
        Value(patch, "/Values/tier").Value<string>().ShouldBe("gold");
        Value(patch, "/Values/plan").Value<string>().ShouldBe("pro");

        // the options in their order, then the new labels of the file, each once
        Labels(patch).ShouldBe(["prod", "ops", "eu"]);
    }

    [Fact]
    public async Task Patch_CustomPropertyWinsOverTheSameNameInTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Values": { "plan": "basic", "other": 1 } }"""));

        var patch = await PatchAsync(Key, ImportFile, command => command.CustomProperties = ["plan=pro"]);

        Paths(patch).ShouldBe(["/Values/plan", "/Values/other"]);
        Value(patch, "/Values/plan").Value<string>().ShouldBe("pro");
        Value(patch, "/Values/other").Value<int>().ShouldBe(1);
    }

    [Fact]
    public async Task Patch_CustomPropertiesKeepTheirTypes()
    {
        var patch = await PatchAsync(Key, configure: command => command.CustomProperties =
        [
            "count=5",
            "enabled=true",
            "ratio=1.5",
            "since=2026-10-24T10:00:00+02:00",
            "name=billing",
        ]);

        Value(patch, "/Values/count").Type.ShouldBe(JTokenType.Integer);
        Value(patch, "/Values/count").Value<long>().ShouldBe(5);
        Value(patch, "/Values/enabled").Type.ShouldBe(JTokenType.Boolean);
        Value(patch, "/Values/ratio").Type.ShouldBe(JTokenType.Float);
        Value(patch, "/Values/ratio").Value<double>().ShouldBe(1.5);
        Value(patch, "/Values/since").Value<string>().ShouldBe("2026-10-24T10:00:00+02:00");
        Value(patch, "/Values/name").Value<string>().ShouldBe("billing");
    }

    [Fact]
    public async Task Patch_EscapesTheNamesOfValues()
    {
        var patch = await PatchAsync(Key, configure: command => command.CustomProperties = ["a/b~c=1"]);

        Paths(patch).ShouldBe(["/Values/a~1b~0c"]);
    }

    // A read keeps these strings as stored (ApiSdk.partial.cs), so a file saved from a read must write the same text.
    [Fact]
    public async Task Patch_DateLikeStringsStayAsWritten()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData(DateLikeFile));

        var patch = await PatchAsync(Key, ImportFile);

        Value(patch, "/Title").Value<string>().ShouldBe("2026-10-24T10:00:00Z");
        Value(patch, "/Values/since").Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        Value(patch, "/Values/nested")["at"]!.Value<string>().ShouldBe("2026-10-24T10:00:00+05:30");
        Value(patch, "/Values/list")[0]!.Value<string>().ShouldBe("2026-10-24T10:00:00.1234567Z");

        // A typed member still parses.
        DateTimeOffset.Parse(Value(patch, "/ValidFrom").Value<string>()!, CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.FromHours(5.5)));
    }

    [Fact]
    public async Task Patch_OfAUnitFromAFile_SetsItsTypeAndDefinition()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "UnitType": "time", "UnitDefinition": "0 2 * * *" }"""));

        var patch = await PatchAsync(UnitKey, ImportFile);

        Paths(patch).ShouldBe(["/UnitType", "/UnitDefinition"]);
        Value(patch, "/UnitType").Value<string>().ShouldBe("time");
        Value(patch, "/UnitDefinition").Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public async Task Patch_OfAUnitWithOnlyItsType_KeepsTheStoredDefinition()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "UnitType": "message" }"""));

        var patch = await PatchAsync(UnitKey, ImportFile);

        Paths(patch).ShouldBe(["/UnitType"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "Title": "Export" }""")]
    public async Task Patch_OfAUnitWithoutThem_KeepsTheStoredOnes(string? content)
    {
        if (content is not null)
        {
            _fileSystem.AddFile(ImportFile, new MockFileData(content));
        }

        var patch = await PatchAsync(UnitKey, content is null ? null : ImportFile);

        Paths(patch).ShouldNotContain("/UnitType");
        Paths(patch).ShouldNotContain("/UnitDefinition");
    }

    [Fact]
    public async Task NotFound_CreatesTheKey_OnlyWhileItIsMissing()
    {
        var body = await CreateAsync(Key, configure: command => command.Labels = ["eu"]);

        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Patch, HttpMethod.Post]);
        var create = _http.Requests[2];
        create.Uri.AbsolutePath.ShouldEndWith($"/annotations/{Key}");
        create.Headers!["If-None-Match"].ShouldBe("*");
        create.Headers.ShouldNotContainKey("If-Match");
        create.Headers["Content-Type"].ShouldBe("application/json");
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
    }

    [Theory]
    [InlineData("rst.billing")]
    [InlineData("sbt.northwind")]
    [InlineData(UnitKey)]
    [InlineData("usg.northwind.billing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData(Key)]
    [InlineData("uxe.northwind.billing.prod.export")]
    public async Task Body_IsWhatTheApiReads(string key)
    {
        var body = await CreateAsync(key);

        var type = AnnotationKey.Parse(key).Type;
        var annotation = (CoreAnnotation)JsonSerializer.Deserialize(body.ToString(), AnnotationTypes.GetRuntimeType(type), ApiOptions)!;

        annotation.AnnotationKey.ShouldBe(key);
        annotation.AnnotationType.ShouldBe(type);
        annotation.Name.ShouldBe(key.Split('.').Last());
        annotation.ProjectName.ShouldBe(AnnotationCommandFixture.Project);
        annotation.ViewName.ShouldBe(AnnotationCommandFixture.View);
    }

    [Theory]
    [InlineData(UnitKey, """{ "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing" }""")]
    [InlineData("usg.northwind.billing", """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing" }""")]
    [InlineData("cnt.northwind.prod", """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind" }""")]
    [InlineData(Key, """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing", "ContextKey": "cnt.northwind.prod", "ContextName": "prod", "UsageKey": "usg.northwind.billing" }""")]
    [InlineData("uxe.northwind.billing.prod.export", """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing", "ContextKey": "cnt.northwind.prod", "ContextName": "prod", "UsageKey": "usg.northwind.billing", "UnitKey": "unt.billing.export", "UnitName": "export", "ExecutionKey": "exe.northwind.billing.prod" }""")]
    public async Task Body_HasTheMembersTheKeyDecides(string key, string members)
    {
        var body = await CreateAsync(key);

        foreach (var (name, value) in JObject.Parse(members))
        {
            body[name]!.Value<string>().ShouldBe(value!.Value<string>(), name);
        }
    }

    [Theory]
    [InlineData("rst.billing")]
    [InlineData("sbt.northwind")]
    [InlineData("usg.northwind.billing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData(Key)]
    public async Task Body_LeavesTheRelationArraysToTheServer(string key)
    {
        var body = await CreateAsync(key);

        body.ContainsKey("UnitNames").ShouldBeFalse();
        body.ContainsKey("ContextNames").ShouldBeFalse();
        body.ContainsKey("ResponsibilityNames").ShouldBeFalse();
    }

    [Fact]
    public async Task Unit_WithoutAFile_HasTheDefaultsOfACreate()
    {
        var body = await CreateAsync(UnitKey);

        body["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        body["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public async Task Unit_FromAFile_KeepsItsTypeAndDefinition()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "UnitType": "time", "UnitDefinition": "0 2 * * *" }"""));

        var body = await CreateAsync(UnitKey, ImportFile);

        body["UnitType"]!.Value<string>().ShouldBe("time");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public async Task Unit_FromAFileWithoutThem_HasTheDefaultsOfACreate()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Export" }"""));

        var body = await CreateAsync(UnitKey, ImportFile);

        body["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        body["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public async Task Body_HasTheContentOfTheFileAndTheOptions()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Billing", "Labels": ["eu"], "Values": { "tier": "gold" } }"""));

        var body = await CreateAsync(Key, ImportFile, command =>
        {
            command.CustomProperties = ["plan=pro"];
            command.Labels = ["prod"];
        });

        body["Title"]!.Value<string>().ShouldBe("Billing");
        body["Labels"]!.Values<string>().ShouldBe(["prod", "eu"]);
        body["Values"]!["tier"]!.Value<string>().ShouldBe("gold");
        body["Values"]!["plan"]!.Value<string>().ShouldBe("pro");
    }

    [Fact]
    public async Task Body_HasEveryEditableMemberOfTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""
            {
              "Title": "Billing",
              "Description": "What it does",
              "DocumentationLink": "https://example.com/runbook",
              "TimeZone": "Europe/Prague",
              "ValidFrom": "2026-10-01T00:00:00+00:00",
              "ExpiresAt": "2027-10-01T00:00:00+00:00",
              "IsDisabled": true,
              "Labels": ["eu"],
              "Values": { "tier": "gold" }
            }
            """));

        var body = await CreateAsync(Key, ImportFile);

        body["Title"]!.Value<string>().ShouldBe("Billing");
        body["Description"]!.Value<string>().ShouldBe("What it does");
        body["DocumentationLink"]!.Value<string>().ShouldBe("https://example.com/runbook");
        body["TimeZone"]!.Value<string>().ShouldBe("Europe/Prague");
        body["ValidFrom"]!.Value<string>().ShouldStartWith("2026-10-01T00:00:00");
        body["ExpiresAt"]!.Value<string>().ShouldStartWith("2027-10-01T00:00:00");
        body["IsDisabled"]!.Value<bool>().ShouldBeTrue();
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
        body["Values"]!["tier"]!.Value<string>().ShouldBe("gold");
    }

    [Fact]
    public async Task Body_DateLikeStringsStayAsWritten()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData(DateLikeFile));

        var body = await CreateAsync(Key, ImportFile);

        body["Title"]!.Value<string>().ShouldBe("2026-10-24T10:00:00Z");
        body["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        body["Values"]!["nested"]!["at"]!.Value<string>().ShouldBe("2026-10-24T10:00:00+05:30");
        body["Values"]!["list"]![0]!.Value<string>().ShouldBe("2026-10-24T10:00:00.1234567Z");

        // A typed member still parses.
        DateTimeOffset.Parse(body["ValidFrom"]!.Value<string>()!, CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.FromHours(5.5)));
    }

    [Fact]
    public async Task Body_CustomPropertyWinsOverTheSameNameInTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Values": { "plan": "basic", "other": 1 } }"""));

        var body = await CreateAsync(Key, ImportFile, command => command.CustomProperties = ["plan=pro"]);

        body["Values"]!["plan"]!.Value<string>().ShouldBe("pro");
        body["Values"]!["other"]!.Value<int>().ShouldBe(1);
    }

    [Theory]
    [InlineData("{ \"Title\": ")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ \"ValidFrom\": \"soon\" }")]
    [InlineData("{ \"Labels\": \"eu\" }")]
    [InlineData("{ \"Values\": [] }")]
    [InlineData("{ \"AnnotationType\": \"Bogus\" }")]
    [InlineData("{ \"UnitType\": { \"a\": 1 } }")]
    public async Task AFileThatIsNoAnnotation_IsWrongInput_AndSendsNothing(string content)
    {
        using var error = new StringWriter();
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        _fileSystem.AddFile(ImportFile, new MockFileData(content));

        var exitCode = await CreateCommand(UnitKey, ImportFile).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        error.ToString().ShouldContain(ImportFile);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATypeCodeInAnotherCase_WritesTheCanonicalKey()
    {
        var body = await CreateAsync("EXE.northwind.billing.prod");

        body["AnnotationKey"]!.Value<string>().ShouldBe(Key);
        _http.Requests[^1].Body!.ShouldContain(Key, Case.Sensitive);
        _http.Requests[^1].Body!.ShouldNotContain("EXE", Case.Sensitive);
        _http.Requests[1].Uri.AbsolutePath.ShouldEndWith($"/annotations/{Key}");
        _http.Requests[^1].Uri.AbsolutePath.ShouldEndWith($"/annotations/{Key}");
    }

    [Fact]
    public async Task AKeyCreatedMeanwhile_IsPatched()
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.NotFound, NotFound)
            .Respond(HttpStatusCode.PreconditionFailed, Exists)
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.Labels = ["eu"]).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Patch, HttpMethod.Post, HttpMethod.Patch]);
        _http.Requests[3].Body.ShouldBe(_http.Requests[1].Body);
        _http.Requests[3].Headers!["Content-Type"].ShouldBe(JsonPatchMediaType);
    }

    [Fact]
    public async Task ThreeRoundsLost_StopWithAConcurrencyError()
    {
        _http.Respond(HttpStatusCode.OK, "[]");

        for (var round = 0; round < StorytellerSetCommand.MaxWriteRounds; round++)
        {
            _http.Respond(HttpStatusCode.NotFound, NotFound).Respond(HttpStatusCode.PreconditionFailed, Exists);
        }

        var exitCode = await CreateCommand(Key).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _http.Requests.Count.ShouldBe(1 + (2 * StorytellerSetCommand.MaxWriteRounds));
        _fixture.Console.Lines.ShouldContain($"The annotation {Key} changed concurrently; try again.");
    }

    // The lookup found nothing deleted; a key deleted after it is not re-created without asking.
    [Fact]
    public async Task AKeyDeletedAfterTheLookup_IsNotReCreated()
    {
        _http.Respond(HttpStatusCode.OK, "[]").Respond(HttpStatusCode.NotFound, Deleted);

        var exitCode = await CreateCommand(Key).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _http.Requests.Count.ShouldBe(2);
        _fixture.Console.Lines.ShouldContain(line => line.Contains("deleted meanwhile", StringComparison.Ordinal) && line.Contains("again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANotFoundWithoutTheCodeOfAKey_PrintsTheServerMessage_AndCreatesNothing()
    {
        _http.Respond(HttpStatusCode.OK, "[]").Respond(HttpStatusCode.NotFound, """{ "Message": "The view does not exist.", "ErrorCode": "ViewNotFound" }""");

        var exitCode = await CreateCommand(Key).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _http.Requests.Count.ShouldBe(2);
        _fixture.Console.Lines.ShouldContain("! The view does not exist.");
        _fixture.Console.Lines.ShouldContain("Error code: ViewNotFound");
    }

    [Fact]
    public async Task AnInvalidPatch_PrintsTheServerMessage()
    {
        _http.Respond(HttpStatusCode.OK, "[]").Respond(HttpStatusCode.BadRequest, """{ "Message": "Title has 201 characters, at most 200 are allowed.", "ErrorCode": "InvalidAnnotation" }""");

        var exitCode = await CreateCommand(Key).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("Title has 201 characters, at most 200 are allowed.");
    }

    [Fact]
    public async Task RestoreAndUpdate_RestoresThenPatches()
    {
        _http.Respond(HttpStatusCode.OK, $"[{Answer(Key)}]")
            .Respond(HttpStatusCode.OK, $$"""{ "Annotations": [{{Answer(Key)}}], "CreatedKeys": [], "Failed": [] }""")
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command =>
        {
            command.RestoreDeleted = true;
            command.Labels = ["eu"];
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post, HttpMethod.Patch]);
        _http.Requests[1].Uri.AbsolutePath.ShouldEndWith("/annotations/deleted/restore");
        _http.Requests[2].Headers!["Content-Type"].ShouldBe(JsonPatchMediaType);
        Labels(ParseArray(_http.Requests[2].Body)).ShouldBe(["eu"]);
    }

    [Fact]
    public async Task CreateNew_CreatesFirst_OnlyWhileTheKeyIsMissing()
    {
        _http.Respond(HttpStatusCode.OK, $"[{Answer(Key)}]").Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.Recreate = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post]);
        _http.Requests[1].Uri.AbsolutePath.ShouldEndWith($"/annotations/{Key}");
        _http.Requests[1].Headers!["If-None-Match"].ShouldBe("*");
        ParseObject(_http.Requests[1].Body)["AnnotationKey"]!.Value<string>().ShouldBe(Key);
    }

    [Fact]
    public async Task CreateNew_OfAKeyRestoredMeanwhile_PatchesIt()
    {
        _http.Respond(HttpStatusCode.OK, $"[{Answer(Key)}]")
            .Respond(HttpStatusCode.PreconditionFailed, Exists)
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.Recreate = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post, HttpMethod.Patch]);
    }

    // The user chose to create the deleted key new, so one deleted again meanwhile is created again.
    [Fact]
    public async Task CreateNew_OfAKeyDeletedAgainMeanwhile_CreatesIt()
    {
        _http.Respond(HttpStatusCode.OK, $"[{Answer(Key)}]")
            .Respond(HttpStatusCode.PreconditionFailed, Exists)
            .Respond(HttpStatusCode.NotFound, Deleted)
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.Recreate = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post, HttpMethod.Patch, HttpMethod.Post]);
        _http.Requests[3].Headers!["If-None-Match"].ShouldBe("*");
    }

    [Fact]
    public async Task Replace_WritesTheInputAlone_OverTheVersionItRead()
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag))
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command =>
        {
            command.IsReplaceRequested = true;
            command.Labels = ["eu"];
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
        _http.Requests[1].Uri.Query.ShouldContain("includeInactive=true");

        var write = _http.Requests[2];
        write.Headers!["If-Match"].ShouldBe(ETag);
        write.Headers.ShouldNotContainKey("If-None-Match");

        var body = ParseObject(write.Body);
        body.ContainsKey("Title").ShouldBeFalse();
        body.ContainsKey("Description").ShouldBeFalse();
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
        ((JObject)body["Values"]!).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Replace_OfAMissingKey_OnlyCreates()
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.NotFound, NotFound)
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.IsReplaceRequested = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests[2].Headers!["If-None-Match"].ShouldBe("*");
        _http.Requests[2].Headers!.ShouldNotContainKey("If-Match");
    }

    [Fact]
    public async Task Replace_OfAReadWithoutATag_WritesWithoutACondition()
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.OK, StoredExecution)
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command => command.IsReplaceRequested = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests[2].Headers!.ShouldNotContainKey("If-Match");
        _http.Requests[2].Headers!.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task Replace_OfAVersionChangedSinceTheRead_PrintsTheServerMessage()
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag))
            .Respond(HttpStatusCode.PreconditionFailed, Changed);

        var exitCode = await CreateCommand(Key, configure: command => command.IsReplaceRequested = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _http.Requests.Count.ShouldBe(3);
        _fixture.Console.Lines.ShouldContain("! The annotation changed since it was read; read it again.");
        _fixture.Console.Lines.ShouldContain("Error code: AnnotationPreconditionFailed");
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replace_ARefusedRead_SendsNoWrite()
    {
        _http.Respond(HttpStatusCode.OK, "[]");
        _http.Respond(HttpStatusCode.Forbidden, """{ "Message": "The machine may not read it.", "ErrorCode": "AnnotationRestricted" }""");

        var exitCode = await CreateCommand(Key, configure: command => command.IsReplaceRequested = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _http.Requests.Count.ShouldBe(2);
        _http.Requests[^1].Method.ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task RestoreAndUpdate_WithReplace_ReplacesTheRestoredVersion()
    {
        _http.Respond(HttpStatusCode.OK, $"[{Answer(Key)}]")
            .Respond(HttpStatusCode.OK, $$"""{ "Annotations": [{{Answer(Key)}}], "CreatedKeys": [], "Failed": [] }""")
            .Respond(HttpStatusCode.OK, StoredExecution, ("ETag", ETag))
            .Respond(HttpStatusCode.OK, Answer(Key));

        var exitCode = await CreateCommand(Key, configure: command =>
        {
            command.RestoreDeleted = true;
            command.IsReplaceRequested = true;
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
        _http.Requests[3].Headers!["If-Match"].ShouldBe(ETag);
    }

    // As before the fix, when the SDK refused a body without AnnotationKey: a plain error and exit code, not a crash.
    [Fact]
    public async Task APatchTheClientCannotWrite_IsAnErrorLine_NotACrash()
    {
        using var error = new StringWriter();
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        _http.Respond(HttpStatusCode.OK, "[]");

        var exitCode = await CreateCommand(UnitKey, client: new UnwritableBodyClient(new HttpClient(_http), refusesPatch: true)).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        error.ToString().ShouldContain($"The annotation {UnitKey} could not be sent");
        error.ToString().ShouldContain("Values.tree");
    }

    [Fact]
    public async Task ABodyTheClientCannotWrite_IsAnErrorLine_NotACrash()
    {
        using var error = new StringWriter();
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        _http.Respond(HttpStatusCode.OK, "[]").Respond(HttpStatusCode.NotFound, NotFound);

        var exitCode = await CreateCommand(UnitKey, client: new UnwritableBodyClient(new HttpClient(_http), refusesPatch: false)).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        error.ToString().ShouldContain($"The annotation {UnitKey} could not be sent");
        error.ToString().ShouldContain("AnnotationKey");
    }

    // A merge into an existing key: the lookup of deleted keys, then the patch.
    private async Task<JArray> PatchAsync(string key, string? importFile = null, Action<StorytellerSetCommand>? configure = null)
    {
        _http.Respond(HttpStatusCode.OK, "[]").Respond(HttpStatusCode.OK, Answer(key));

        (await CreateCommand(key, importFile, configure).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _http.Requests.Count.ShouldBe(2);
        _http.Requests[1].Method.ShouldBe(HttpMethod.Patch);
        return ParseArray(_http.Requests[1].Body);
    }

    // A merge into a missing key: the lookup, the patch (404), then the create.
    private async Task<JObject> CreateAsync(string key, string? importFile = null, Action<StorytellerSetCommand>? configure = null)
    {
        _http.Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.NotFound, NotFound)
            .Respond(HttpStatusCode.OK, Answer(key));

        (await CreateCommand(key, importFile, configure).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _http.Requests.Count.ShouldBe(3);
        _http.Requests[2].Method.ShouldBe(HttpMethod.Post);
        return ParseObject(_http.Requests[2].Body);
    }

    private StorytellerSetCommand CreateCommand(string key, string? importFile = null, Action<StorytellerSetCommand>? configure = null, AnnotationsApiClient? client = null)
    {
        var command = new StorytellerSetCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            client ?? new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = "http://localhost:7071/api" }),
            Options.Create(new AccessDefaultOptions()),
            _fileSystem)
        {
            AnnotationKey = key,
            ImportFilePath = importFile,
        };

        configure?.Invoke(command);
        return command;
    }

    // Without parsing dates: the assertions are about the text that was sent.
    private static JToken Parse(string? body)
    {
        using var reader = new JsonTextReader(new StringReader(body!)) { DateParseHandling = DateParseHandling.None };
        return JToken.Load(reader);
    }

    private static JArray ParseArray(string? body)
    {
        return (JArray)Parse(body);
    }

    private static JObject ParseObject(string? body)
    {
        return (JObject)Parse(body);
    }

    private static List<string> Paths(JArray patch)
    {
        return patch.Select(operation => operation["path"]!.Value<string>()!).ToList();
    }

    // The value of the one operation on the path.
    private static JToken Value(JArray patch, string path)
    {
        return patch.Single(operation => operation["path"]!.Value<string>() == path)["value"]!;
    }

    // The appended labels, in their order.
    private static List<string> Labels(JArray patch)
    {
        return patch
            .Where(operation => operation["path"]!.Value<string>() == "/Labels/-")
            .Select(operation => operation["value"]!.Value<string>()!)
            .ToList();
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

    // The SDK's refusal of a request body: of the patch, or of the create after it (as it threw for a missing required
    // member).
    private sealed class UnwritableBodyClient(HttpClient httpClient, bool refusesPatch)
        : AnnotationsApiClient(httpClient, new SdkConfiguration { BaseUrl = "http://localhost:7071/api" })
    {
        public override Task<VersionedResponse<SdkAnnotation>> PatchAnnotationVersionedAsync(string organization, string project, string view, string key, JToken patch, string? ifMatch = null, CancellationToken cancellationToken = default)
        {
            return refusesPatch
                ? throw new JsonSerializationException("Self referencing loop detected for property 'tree'. Path 'Values.tree'.")
                : base.PatchAnnotationVersionedAsync(organization, project, view, key, patch, ifMatch, cancellationToken);
        }

        public override Task<SdkAnnotation> SetAnnotationAsync(string organization, string project, string view, string key, SdkAnnotation body, string if_Match = null!, string if_None_Match = null!, CancellationToken cancellationToken = default)
        {
            throw new JsonSerializationException("Cannot write a null value for property 'AnnotationKey'. Property requires a value. Path ''.");
        }
    }
}
