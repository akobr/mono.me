using System.Text;

using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// PATCH of an annotation: the media types, the conditions and the answers.
public partial class AnnotationsHttpTests
{
    private const string MergePatchType = "application/merge-patch+json";
    private const string JsonPatchType = "application/json-patch+json";

    [Theory]
    [InlineData(MergePatchType, """{"Title":"patched"}""", AnnotationPatchFormat.MergePatch)]
    [InlineData(MergePatchType + "; charset=utf-8", """{"title":"patched"}""", AnnotationPatchFormat.MergePatch)]
    [InlineData(JsonPatchType, """[{"op":"replace","path":"/Title","value":"patched"}]""", AnnotationPatchFormat.JsonPatch)]
    [InlineData("application/json", """{"Title":"patched"}""", AnnotationPatchFormat.MergePatch)]
    [InlineData("application/json", """[{"op":"add","path":"/Title","value":"patched"}]""", AnnotationPatchFormat.JsonPatch)]
    [InlineData(null, """{"Title":"patched"}""", AnnotationPatchFormat.MergePatch)]
    [InlineData(null, """[{"op":"add","path":"/Title","value":"patched"}]""", AnnotationPatchFormat.JsonPatch)]
    public async Task PatchAnnotation_AppliesThePatchOfItsFormat_AndAnswersTheTag(string? contentType, string body, AnnotationPatchFormat format)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") with { Description = "kept" }, WrittenMarker = WrittenMarker };
        var request = WithContext(Patch(body, contentType));

        var result = await Http(annotations).PatchAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        var stored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Subject>();
        stored.Title.ShouldBe("patched");
        stored.Description.ShouldBe("kept");
        annotations.LastPatch!.Format.ShouldBe(format);
        annotations.LastCondition!.IsEmpty.ShouldBeTrue();
        annotations.Calls.ShouldBe(["patch:acme:sbt.northwind"]);
        annotations.LastActor!.Id.ShouldBe("user_01");
        ConfigurationConditionalReadTests.Headers(request).ETag.ToString().ShouldBe(WrittenTag);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/merge-patch+xml")]
    [InlineData("not a media type")]
    public async Task PatchAnnotation_OfAnotherMediaType_IsUnsupported_AndNamesThePatchFormats(string contentType)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };
        var request = WithContext(Patch("""{"Title":"patched"}""", contentType));

        var result = await Http(annotations).PatchAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        var answer = result.ShouldBeOfType<ObjectResult>();
        answer.StatusCode.ShouldBe(415);
        answer.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidContentType);
        ConfigurationConditionalReadTests.Headers(request)["Accept-Patch"].ToString().ShouldBe($"{MergePatchType}, {JsonPatchType}");
        annotations.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MergePatchType, """[{"op":"add","path":"/Title","value":"x"}]""")]
    [InlineData(JsonPatchType, """{"Title":"x"}""")]
    [InlineData("application/json", "\"x\"")]
    [InlineData("application/json", "null")]
    [InlineData(MergePatchType, "{not json")]
    [InlineData(MergePatchType, "")]
    [InlineData(MergePatchType, """{"Title":"a","Title":"b"}""")]
    [InlineData(JsonPatchType, """[{"op":"nope","path":"/Title"}]""")]
    [InlineData(JsonPatchType, """[{"op":"add","value":1}]""")]
    [InlineData(JsonPatchType, "[null]")]
    public async Task PatchAnnotation_ABodyThatIsNotAPatch_IsABadRequest_AndReadsNothing(string contentType, string body)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };

        var result = await Http(annotations).PatchAnnotation(Patch(body, contentType), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.PatchInvalid);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task PatchAnnotation_WithABom_IsRead()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"Title":"patched"}""")).ToArray();
        var request = Request("PATCH", body, new Dictionary<string, string> { ["Content-Type"] = MergePatchType });

        var result = await Http(annotations).PatchAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Subject>().Title.ShouldBe("patched");
    }

    [Fact]
    public async Task PatchAnnotation_IfMatch_ReachesTheService()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };
        var request = Patch("""{"Title":"patched"}""", MergePatchType);
        request.Headers.TryAddWithoutValidation("If-Match", StoredTag);

        await Http(annotations).PatchAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        annotations.LastCondition!.IfMatch!.Markers.ShouldBe([StoredMarker]);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("\"a1.IjEi\"")]
    public async Task PatchAnnotation_IfNoneMatch_IsABadRequest(string value)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };
        var request = Patch("""{"Title":"patched"}""", MergePatchType);
        request.Headers.TryAddWithoutValidation("If-None-Match", value);

        var result = await Http(annotations).PatchAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidPrecondition);
        annotations.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ErrorCodes.AnnotationNotFound, "write it whole with POST")]
    [InlineData(ErrorCodes.AnnotationDeleted, "restore it")]
    public async Task PatchAnnotation_OfAMissingOrDeletedKey_IsNotFound_WithAHint(string errorCode, string hint)
    {
        var annotations = new RecordingAnnotations { Failure = new NotFoundException("The annotation 'sbt.northwind' is not there.", errorCode) };

        var result = await Http(annotations).PatchAnnotation(Patch("""{"Title":"x"}""", MergePatchType), "acme", "billing", "default", "sbt.northwind");

        var answer = result.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        answer.ErrorCode.ShouldBe(errorCode);
        answer.Hint!.ShouldContain(hint);
    }

    public static TheoryData<string, int, string> Failures => new()
    {
        { "precondition", 412, ErrorCodes.AnnotationPreconditionFailed },
        { "test", 412, ErrorCodes.PatchTestFailed },
        { "operation", 400, ErrorCodes.PatchInvalid },
        { "invalid", 400, ErrorCodes.InvalidAnnotation },
        { "conflict", 409, ErrorCodes.Conflict },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PatchAnnotation_AFailureOfTheWrite_AnswersItsStatus(string failure, int status, string errorCode)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") with { Title = "stored" } };
        var body = failure switch
        {
            "test" => """[{"op":"test","path":"/Title","value":"other"},{"op":"replace","path":"/Title","value":"x"}]""",
            "operation" => """[{"op":"remove","path":"/Values/missing"}]""",
            "invalid" => """[{"op":"add","path":"/Unknown","value":1}]""",
            _ => "[]",
        };

        annotations = failure switch
        {
            "precondition" => new RecordingAnnotations { Failure = new PreconditionFailedException("changed", ErrorCodes.AnnotationPreconditionFailed) },
            "conflict" => new RecordingAnnotations { Failure = new ConflictException("The annotations changed concurrently; try again.") },
            _ => annotations,
        };

        var result = await Http(annotations).PatchAnnotation(Patch(body, JsonPatchType), "acme", "billing", "default", "sbt.northwind");

        var answer = result.ShouldBeAssignableTo<ObjectResult>()!;
        answer.StatusCode.ShouldBe(status);
        answer.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(errorCode);
    }

    [Fact]
    public async Task PatchAnnotation_OfAnInvalidKey_IsABadRequest()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).PatchAnnotation(Patch("""{"Title":"x"}""", MergePatchType), "acme", "billing", "default", "nope.x");

        result.ShouldBeOfType<BadRequestObjectResult>();
        annotations.Calls.ShouldBeEmpty();
    }

    // The contract in the document: both patch formats, the condition, the tag and the answers of a patch.
    [Fact]
    public async Task Document_PatchAnnotation_IsDeclared()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var path = document.RootElement.GetProperty("paths").EnumerateObject()
            .Single(entry => entry.Value.TryGetProperty("patch", out var patch) && patch.GetProperty("operationId").GetString() == Definitions.RouteIds.Annotations.PatchAnnotation);
        var operation = path.Value.GetProperty("patch");

        path.Name.ShouldBe("/v1/{organization}/{project}/{view}/annotations/{key}");
        operation.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).ShouldBe(
            [Definitions.Parameters.Organization, Definitions.Parameters.Project, Definitions.Parameters.View, Definitions.Parameters.Key, Definitions.Parameters.IfMatch]);

        var body = operation.GetProperty("requestBody");
        body.GetProperty("required").GetBoolean().ShouldBeTrue();
        var content = body.GetProperty("content");
        content.EnumerateObject().Select(type => type.Name).ShouldBe([MergePatchType, JsonPatchType], ignoreOrder: true);
        content.GetProperty(MergePatchType).GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/AnnotationMergePatch");
        content.GetProperty(JsonPatchType).GetProperty("schema").GetProperty("type").GetString().ShouldBe("array");

        var responses = operation.GetProperty("responses");
        responses.EnumerateObject().Select(response => response.Name).ShouldBe(["200", "400", "404", "409", "412", "415", "401", "403", "500"], ignoreOrder: true);
        responses.GetProperty("200").GetProperty("headers").GetProperty(HeaderNames.ETag).GetProperty("description").GetString().ShouldBe(Definitions.Descriptions.AnnotationEntityTag);

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AnnotationMergePatch");
        (schema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0).ShouldBeFalse();
        var members = schema.GetProperty("properties").EnumerateObject().ToList();
        members.Select(member => member.Name).ShouldBe(
            ["IsDisabled", "ValidFrom", "ExpiresAt", "TimeZone", "Title", "Description", "DocumentationLink", "Labels", "Values", "UnitType", "UnitDefinition"],
            ignoreOrder: true);
        members.ShouldAllBe(member => member.Value.GetProperty("nullable").GetBoolean());
    }

    private static TestHttpRequestData Patch(string body, string? contentType)
    {
        return Request("PATCH", Encoding.UTF8.GetBytes(body), contentType is null ? [] : new Dictionary<string, string> { ["Content-Type"] = contentType });
    }
}
