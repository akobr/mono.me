using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Commands.Schemas;
using _42.Platform.Cli.Commands.Templates;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Cli.UnitTests.Services;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// Each edit command opened and saved without an edit, through the real client and the real editor service: a document
// with date-like strings, read as the SDK reads it, compares equal to the saved file, so nothing is written.
public class EditCommandsDateTests
{
    private const string Key = "exe.northwind.invoicing.prod";

    // An offset other than this machine's, and a date alone: parsed, both would come back as other text.
    private const string Content = """{ "renewal": "2026-10-20T12:00:00.1230000+05:30", "day": "2026-10-20", "nested": { "at": "2026-10-20T12:00:00Z" } }""";
    private const string Modified = """{ "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } }""";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly ScriptedHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task ConfigEdit_WithoutAnEdit_WritesNothing()
    {
        RespondWithStoredConfiguration();

        var exitCode = await new ConfigEditCommand(_fixture.Console.Object, _fixture.Context.Object, Configurations(), Editor(), Options.Create(ScriptedEditor.Vim))
        {
            AnnotationKey = Key,
        }.OnExecuteAsync();

        ShouldHaveWrittenNothing(exitCode, 2);
    }

    [Fact]
    public async Task ConfigPatch_WithoutAnEdit_WritesNothing()
    {
        RespondWithStoredConfiguration();

        var exitCode = await new ConfigPatchCommand(_fixture.Console.Object, _fixture.Context.Object, Configurations(), Editor(), Options.Create(ScriptedEditor.Vim), _fileSystem)
        {
            AnnotationKey = Key,
        }.OnExecuteAsync();

        ShouldHaveWrittenNothing(exitCode, 2);
    }

    [Fact]
    public async Task TemplateEdit_WithoutAnEdit_WritesNothing()
    {
        _http.Respond(HttpStatusCode.OK, $$"""{ "AnnotationType": "Execution", "Version": 3, "Content": {{Content}}, "Modified": {{Modified}} }""");
        var templates = new TemplatesApiClient(new HttpClient(_http), Sdk());

        var exitCode = await new TemplateEditCommand(_fixture.Console.Object, _fixture.Context.Object, templates, Editor(), Options.Create(ScriptedEditor.Vim))
        {
            AnnotationType = "exe",
        }.OnExecuteAsync();

        ShouldHaveWrittenNothing(exitCode, 1);
    }

    [Fact]
    public async Task SchemaEdit_WithoutAnEdit_WritesNothing()
    {
        _http.Respond(HttpStatusCode.OK, $$"""{ "View": "default", "AnnotationType": "Execution", "Version": 2, "Content": {{Content}}, "Modified": {{Modified}} }""");
        var schemas = new SchemasApiClient(new HttpClient(_http), Sdk());

        var exitCode = await SchemaOperations.EditAsync(
            _fixture.Console.Object,
            schemas,
            _fixture.Context.Object,
            Editor(),
            ScriptedEditor.Vim,
            SchemaLayer.Type,
            "exe",
            null,
            false);

        ShouldHaveWrittenNothing(exitCode, 1);
    }

    // The versions, then the current one, as GetStoredConfigurationAsync reads them.
    private void RespondWithStoredConfiguration()
    {
        _http.Respond(HttpStatusCode.OK, $$"""[{ "Version": 4, "Modified": {{Modified}}, "ExpirationTime": "9999-12-31T23:59:59.9999999+00:00" }]""");
        _http.Respond(HttpStatusCode.OK, $$"""{ "AnnotationKey": "{{Key}}", "Version": 4, "Content": {{Content}}, "Modified": {{Modified}} }""");
    }

    private void ShouldHaveWrittenNothing(int exitCode, int reads)
    {
        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Console.Lines.ShouldContain("No changes detected.");
        _http.Requests.Count.ShouldBe(reads);
        _http.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    // Opens the file and leaves it as it is.
    private ScriptedEditor Editor()
    {
        return new ScriptedEditor(_fileSystem, [null]);
    }

    private ConfigurationsApiClient Configurations()
    {
        return new ConfigurationsApiClient(new HttpClient(_http), Sdk());
    }

    private static SdkConfiguration Sdk()
    {
        return new SdkConfiguration { BaseUrl = "http://localhost:7071/api" };
    }
}
