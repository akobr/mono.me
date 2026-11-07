using System;
using System.Globalization;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
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

namespace _42.Platform.Cli.UnitTests.Commands;

// The body of sform story set through the real generated client, which serializes it as in production: the mocked client
// of StorytellerSetCommandTests never does, and the SDK's required members are checked only there.
public class StorytellerSetBodyTests
{
    private const string UnitKey = "unt.billing.export";
    private const string ImportFile = "annotation.json";
    private const string NotFound = """{ "Message": "The annotation does not exist.", "ErrorCode": "AnnotationNotFound" }""";

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

    private const string StoredUnit = """
        {
          "AnnotationType": "Unit", "ProjectName": "proj1", "ViewName": "default",
          "AnnotationKey": "unt.billing.export", "Name": "export", "UnitType": "time", "UnitDefinition": "0 2 * * *"
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

    [Theory]
    [InlineData("rst.billing")]
    [InlineData("sbt.northwind")]
    [InlineData(UnitKey)]
    [InlineData("usg.northwind.billing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData("exe.northwind.billing.prod")]
    [InlineData("uxe.northwind.billing.prod.export")]
    public async Task Body_IsWhatTheApiReads(string key)
    {
        var body = await SetAsync(key);

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
    [InlineData("exe.northwind.billing.prod", """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing", "ContextKey": "cnt.northwind.prod", "ContextName": "prod", "UsageKey": "usg.northwind.billing" }""")]
    [InlineData("uxe.northwind.billing.prod.export", """{ "SubjectKey": "sbt.northwind", "SubjectName": "northwind", "ResponsibilityKey": "rst.billing", "ResponsibilityName": "billing", "ContextKey": "cnt.northwind.prod", "ContextName": "prod", "UsageKey": "usg.northwind.billing", "UnitKey": "unt.billing.export", "UnitName": "export", "ExecutionKey": "exe.northwind.billing.prod" }""")]
    public async Task Body_HasTheMembersTheKeyDecides(string key, string members)
    {
        var body = await SetAsync(key);

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
    [InlineData("exe.northwind.billing.prod")]
    public async Task Body_LeavesTheRelationArraysToTheServer(string key)
    {
        var body = await SetAsync(key);

        body.ContainsKey("UnitNames").ShouldBeFalse();
        body.ContainsKey("ContextNames").ShouldBeFalse();
        body.ContainsKey("ResponsibilityNames").ShouldBeFalse();
    }

    [Fact]
    public async Task Unit_WithoutAFile_HasTheDefaultsOfACreate()
    {
        var body = await SetAsync(UnitKey);

        body["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        body["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public async Task Unit_FromAFile_KeepsItsTypeAndDefinition()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "UnitType": "time", "UnitDefinition": "0 2 * * *" }"""));

        var body = await SetAsync(UnitKey, ImportFile);

        body["UnitType"]!.Value<string>().ShouldBe("time");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public async Task Unit_FromAFileWithoutThem_HasTheDefaultsOfACreate()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Export" }"""));

        var body = await SetAsync(UnitKey, ImportFile);

        body["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        body["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public async Task Body_HasTheContentOfTheFileAndTheOptions()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "Billing", "Labels": ["eu"], "Values": { "tier": "gold" } }"""));

        var body = await SetAsync("exe.northwind.billing.prod", ImportFile, command =>
        {
            command.CustomProperties = ["plan=pro"];
            command.Labels = ["prod"];
        });

        body["Title"]!.Value<string>().ShouldBe("Billing");
        body["Labels"]!.Values<string>().ShouldBe(["prod", "eu"], ignoreOrder: true);
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

        var body = await SetAsync("exe.northwind.billing.prod", ImportFile);

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

    // A read keeps these strings as stored (ApiSdk.partial.cs), so a file saved from a read must write the same text.
    [Fact]
    public async Task DateLikeStrings_StayAsWritten()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""
            {
              "Title": "2026-10-24T10:00:00Z",
              "ValidFrom": "2026-10-24T10:00:00+05:30",
              "Values": {
                "since": "2026-10-24T10:00:00.000+02:00",
                "nested": { "at": "2026-10-24T10:00:00+05:30" },
                "list": [ "2026-10-24T10:00:00.1234567Z" ]
              }
            }
            """));

        var body = await SetAsync("exe.northwind.billing.prod", ImportFile);

        body["Title"]!.Value<string>().ShouldBe("2026-10-24T10:00:00Z");
        body["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        body["Values"]!["nested"]!["at"]!.Value<string>().ShouldBe("2026-10-24T10:00:00+05:30");
        body["Values"]!["list"]![0]!.Value<string>().ShouldBe("2026-10-24T10:00:00.1234567Z");

        // A typed member still parses.
        DateTimeOffset.Parse(body["ValidFrom"]!.Value<string>()!, CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.FromHours(5.5)));
    }

    [Fact]
    public async Task CustomProperty_WinsOverTheSameNameInTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Values": { "plan": "basic", "other": 1 } }"""));

        var body = await SetAsync("exe.northwind.billing.prod", ImportFile, command => command.CustomProperties = ["plan=pro"]);

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

        var exitCode = await ExecuteAsync(UnitKey, ImportFile);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        error.ToString().ShouldContain(ImportFile);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATypeCodeInAnotherCase_WritesTheCanonicalKey()
    {
        var body = await SetAsync("EXE.northwind.billing.prod");

        body["AnnotationKey"]!.Value<string>().ShouldBe("exe.northwind.billing.prod");
        _http.Requests[0].Body!.ShouldContain("exe.northwind.billing.prod", Case.Sensitive);
        _http.Requests[0].Body!.ShouldNotContain("EXE", Case.Sensitive);
        _http.Requests[^1].Uri.AbsolutePath.ShouldEndWith("/annotations/exe.northwind.billing.prod");
    }

    [Fact]
    public async Task Merge_KeepsWhatTheInputDoesNotHold()
    {
        var body = await SetAsync(
            "exe.northwind.billing.prod",
            configure: command =>
            {
                command.CustomProperties = ["plan=pro"];
                command.Labels = ["eu"];
            },
            stored: StoredExecution);

        _http.Requests[1].Method.ShouldBe(HttpMethod.Get);
        _http.Requests[1].Uri.Query.ShouldContain("includeInactive=true");
        body["Title"]!.Value<string>().ShouldBe("Stored");
        body["Description"]!.Value<string>().ShouldBe("Stored description");
        body["DocumentationLink"]!.Value<string>().ShouldBe("https://example.com/stored");
        body["TimeZone"]!.Value<string>().ShouldBe("Europe/Prague");
        body["ValidFrom"]!.Value<string>().ShouldStartWith("2026-01-01T00:00:00");
        body["IsDisabled"]!.Value<bool>().ShouldBeTrue();
        body["Labels"]!.Values<string>().ShouldBe(["stored", "eu"]);
        body["Values"]!["tier"]!.Value<string>().ShouldBe("gold");
        body["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        body["Values"]!["nested"]!["at"]!.Value<int>().ShouldBe(1);
        body["Values"]!["plan"]!.Value<string>().ShouldBe("pro");
        body.ContainsKey("UnitNames").ShouldBeFalse();
    }

    [Fact]
    public async Task Merge_TheFileWinsOverTheStoredMembers_AndTheOptionsOverTheFile()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "Title": "From the file", "IsDisabled": false, "Labels": ["stored"], "Values": { "tier": "silver", "plan": "free" } }"""));

        var body = await SetAsync("exe.northwind.billing.prod", ImportFile, command => command.CustomProperties = ["plan=pro"], StoredExecution);

        body["Title"]!.Value<string>().ShouldBe("From the file");
        body["Description"]!.Value<string>().ShouldBe("Stored description");
        body["IsDisabled"]!.Value<bool>().ShouldBeFalse();
        body["Labels"]!.Values<string>().ShouldBe(["stored"]);
        body["Values"]!["tier"]!.Value<string>().ShouldBe("silver");
        body["Values"]!["plan"]!.Value<string>().ShouldBe("pro");
        body["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
    }

    [Fact]
    public async Task Merge_AStoredUnit_KeepsItsTypeAndDefinition()
    {
        var body = await SetAsync(UnitKey, stored: StoredUnit);

        body["UnitType"]!.Value<string>().ShouldBe("time");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public async Task Merge_AUnitOfTheFile_WinsOverTheStoredOne()
    {
        _fileSystem.AddFile(ImportFile, new MockFileData("""{ "UnitType": "message" }"""));

        var body = await SetAsync(UnitKey, ImportFile, stored: StoredUnit);

        body["UnitType"]!.Value<string>().ShouldBe("message");
        body["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public async Task Replace_ReadsNothing_AndSendsTheInputAlone()
    {
        var body = await SetAsync("exe.northwind.billing.prod", configure: command =>
        {
            command.IsReplaceRequested = true;
            command.Labels = ["eu"];
        });

        _http.Requests.Count.ShouldBe(2);
        _http.Requests.ShouldNotContain(request => request.Method == HttpMethod.Get);
        body.ContainsKey("Title").ShouldBeFalse();
        body["Labels"]!.Values<string>().ShouldBe(["eu"]);
        ((JObject)body["Values"]!).Count.ShouldBe(0);
    }

    [Fact]
    public async Task ARefusedRead_SendsNoWrite()
    {
        _http.Respond(HttpStatusCode.OK, "[]");
        _http.Respond(HttpStatusCode.Forbidden, """{ "Message": "The machine may not read it.", "ErrorCode": "AnnotationRestricted" }""");

        var exitCode = await CreateCommand("exe.northwind.billing.prod").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _http.Requests.Count.ShouldBe(2);
        _http.Requests[^1].Method.ShouldBe(HttpMethod.Get);
    }

    // As before the fix, when the SDK refused a body without AnnotationKey: a plain error and exit code, not a crash.
    [Fact]
    public async Task ABodyTheClientCannotWrite_IsAnErrorLine_NotACrash()
    {
        using var error = new StringWriter();
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        _http.Respond(HttpStatusCode.OK, "[]");
        _http.Respond(HttpStatusCode.NotFound, NotFound);

        var command = new StorytellerSetCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            new UnwritableBodyClient(new HttpClient(_http)),
            Options.Create(new AccessDefaultOptions()),
            _fileSystem)
        {
            AnnotationKey = UnitKey,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        error.ToString().ShouldContain($"The annotation {UnitKey} could not be sent");
        error.ToString().ShouldContain("AnnotationKey");
    }

    [Fact]
    public async Task Write_GoesToTheRouteOfTheKey()
    {
        await SetAsync(UnitKey);

        var write = _http.Requests[^1];
        write.Uri.AbsolutePath.ShouldBe($"/api/v1/{AnnotationCommandFixture.Organization}/{AnnotationCommandFixture.Project}/{AnnotationCommandFixture.View}/annotations/{UnitKey}");
    }

    private async Task<JObject> SetAsync(string key, string? importFile = null, Action<StorytellerSetCommand>? configure = null, string? stored = null)
    {
        (await ExecuteAsync(key, importFile, configure, stored)).ShouldBe(ExitCodes.SUCCESS);

        // Without parsing dates: the assertions are about the text that was sent.
        using var reader = new JsonTextReader(new StringReader(_http.Requests[^1].Body!)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    // The lookup of deleted keys, the read of what a merge goes into (404 without a stored annotation, none with
    // --replace), then the write.
    private Task<int> ExecuteAsync(string key, string? importFile = null, Action<StorytellerSetCommand>? configure = null, string? stored = null)
    {
        var command = CreateCommand(key, importFile);
        configure?.Invoke(command);

        _http.Respond(HttpStatusCode.OK, "[]");

        if (!command.IsReplaceRequested)
        {
            _http.Respond(stored is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, stored ?? NotFound);
        }

        _http.Respond(HttpStatusCode.OK, Answer(key));
        return command.OnExecuteAsync();
    }

    private StorytellerSetCommand CreateCommand(string key, string? importFile = null)
    {
        return new StorytellerSetCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = "http://localhost:7071/api" }),
            Options.Create(new AccessDefaultOptions()),
            _fileSystem)
        {
            AnnotationKey = key,
            ImportFilePath = importFile,
        };
    }

    // The SDK's refusal of a body, as it threw for a missing required member.
    private sealed class UnwritableBodyClient(HttpClient httpClient)
        : AnnotationsApiClient(httpClient, new SdkConfiguration { BaseUrl = "http://localhost:7071/api" })
    {
        public override Task<_42.Platform.Storyteller.Sdk.Annotation> SetAnnotationAsync(string organization, string project, string view, string key, _42.Platform.Storyteller.Sdk.Annotation body = null!, System.Threading.CancellationToken cancellationToken = default)
        {
            throw new JsonSerializationException("Cannot write a null value for property 'AnnotationKey'. Property requires a value. Path ''.");
        }
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
}
