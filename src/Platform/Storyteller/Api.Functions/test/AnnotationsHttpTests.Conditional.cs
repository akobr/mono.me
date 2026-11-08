using System.Text;

using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ConditionalWrites;
using _42.Platform.Storyteller.Api.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The version of an annotation over HTTP: the ETag of its reads and writes, and the conditions of its writes.
public partial class AnnotationsHttpTests
{
    private const string StoredMarker = "\"00000a1b-0000-0d00-0000-6a0f1c2d0000\"";
    private const string WrittenMarker = "\"00000a1c-0000-0d00-0000-6a0f1c2e0000\"";

    private static readonly string StoredTag = AnnotationEntityTags.Create(StoredMarker).ToString();
    private static readonly string WrittenTag = AnnotationEntityTags.Create(WrittenMarker).ToString();

    [Fact]
    public async Task GetAnnotation_AnswersTheTagOfItsVersion()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind"), StoredMarker = StoredMarker };
        var request = WithContext(Request("GET"));

        var result = await Http(annotations).GetAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<OkObjectResult>();
        var tag = ConfigurationConditionalReadTests.Headers(request).ETag.ToString();
        tag.ShouldBe(StoredTag);
        tag.ShouldStartWith("\"a1.");
        EntityTagHeaderValue.Parse(tag).IsWeak.ShouldBeFalse();
    }

    [Fact]
    public async Task GetAnnotation_OfADeletedAnnotation_AnswersNoTag()
    {
        var annotations = new RecordingAnnotations { StoredDeleted = Deleted(Subject("sbt.northwind")), StoredMarker = StoredMarker };
        var request = WithContext(Request("GET"));

        var result = await Http(annotations).GetAnnotation(request, "acme", "billing", "default", "sbt.northwind", includeInactive: true, includeDeleted: true);

        result.ShouldBeOfType<OkObjectResult>();
        ConfigurationConditionalReadTests.Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    [Fact]
    public async Task GetAnnotation_ThatIsInactive_AnswersNoTag()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") with { IsDisabled = true }, StoredMarker = StoredMarker };
        var request = WithContext(Request("GET"));

        var result = await Http(annotations).GetAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<NotFoundObjectResult>();
        ConfigurationConditionalReadTests.Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    // sform story set --replace reads with includeInactive and writes with the tag of that read.
    [Fact]
    public async Task GetAnnotation_ThatIsInactive_ReadWithIncludeInactive_AnswersItsTag()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") with { IsDisabled = true }, StoredMarker = StoredMarker };
        var request = WithContext(Request("GET"));

        var result = await Http(annotations).GetAnnotation(request, "acme", "billing", "default", "sbt.northwind", includeInactive: true);

        result.ShouldBeOfType<OkObjectResult>();
        ConfigurationConditionalReadTests.Headers(request).ETag.ToString().ShouldBe(StoredTag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("absent")]
    public async Task GetAnnotation_WithoutAMarker_AnswersNoTag(string? marker)
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind"), StoredMarker = marker };
        var request = WithContext(Request("GET"));

        await Http(annotations).GetAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        ConfigurationConditionalReadTests.Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    [Fact]
    public async Task SetAnnotation_ByPost_AnswersTheTagOfTheWrittenVersion()
    {
        var annotations = new RecordingAnnotations { WrittenMarker = WrittenMarker };
        var request = WithContext(Request("POST", UsageBody));

        var result = await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<OkObjectResult>();
        ConfigurationConditionalReadTests.Headers(request).ETag.ToString().ShouldBe(WrittenTag);
        annotations.LastCondition.ShouldNotBeNull().IsEmpty.ShouldBeTrue();
    }

    // RFC 9110 §9.3.4: the server normalizes the body of a PUT, so its answer carries no validator.
    [Fact]
    public async Task SetAnnotation_ByPut_AnswersNoTag()
    {
        var annotations = new RecordingAnnotations { WrittenMarker = WrittenMarker };
        var request = WithContext(Request("PUT", UsageBody));

        var result = await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<OkObjectResult>();
        ConfigurationConditionalReadTests.Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    [Fact]
    public async Task SetAnnotation_IfMatch_ReachesTheServiceAsTheMarkersOfTheTags()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation("If-Match", $"{StoredTag}, {WrittenTag}");

        await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        var condition = annotations.LastCondition.ShouldNotBeNull();
        condition.IfNoneMatch.ShouldBeNull();
        var ifMatch = condition.IfMatch.ShouldNotBeNull();
        ifMatch.Any.ShouldBeFalse();
        ifMatch.Markers.ShouldBe([StoredMarker, WrittenMarker]);
    }

    [Fact]
    public async Task SetAnnotation_IfMatchOnSeveralLines_ReadsEveryLine()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation("If-Match", [StoredTag, WrittenTag]);

        await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        annotations.LastCondition!.IfMatch!.Markers.ShouldBe([StoredMarker, WrittenMarker], ignoreOrder: true);
    }

    [Fact]
    public async Task SetAnnotation_IfMatchAny_And_IfNoneMatchAny_ReachTheService()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("POST", UsageBody);
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        annotations.LastCondition!.IfMatch!.Any.ShouldBeTrue();
        annotations.LastCondition.IfNoneMatch!.Any.ShouldBeTrue();
    }

    // A weak tag never passes a strong comparison, and a tag of another kind names no version: both stay in the list
    // as nothing, so the condition matches no version and the write answers 412.
    [Theory]
    [InlineData("W/\"a1.IjEi\"")]
    [InlineData("\"e.AAECAwQFBgcICQoLDA0ODw\"")]
    [InlineData("\"a1.!!\"")]
    [InlineData("\"anything\"")]
    public async Task SetAnnotation_IfMatchOfAnotherKindOfTag_MatchesNoVersion(string tag)
    {
        var annotations = new RecordingAnnotations();
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation("If-Match", tag);

        await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        var ifMatch = annotations.LastCondition!.IfMatch.ShouldNotBeNull();
        ifMatch.Any.ShouldBeFalse();
        ifMatch.Markers.ShouldBeEmpty();
        annotations.LastCondition.Holds(exists: true, "\"1\"").ShouldBeFalse();
    }

    // If-None-Match compares weakly, so the weak form of a tag names its version.
    [Fact]
    public async Task SetAnnotation_IfNoneMatchOfAWeakTag_NamesItsVersion()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/" + StoredTag);

        await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        annotations.LastCondition!.IfNoneMatch!.Markers.ShouldBe([StoredMarker]);
    }

    [Theory]
    [InlineData("If-Match", "not-a-tag")]
    [InlineData("If-Match", "\"a\" \"b\"")]
    [InlineData("If-Match", "*, \"a\"")]
    [InlineData("If-Match", "")]
    [InlineData("If-None-Match", "W/")]
    public async Task SetAnnotation_AMalformedCondition_IsABadRequest_AndWritesNothing(string header, string value)
    {
        var annotations = new RecordingAnnotations();
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation(header, value);

        var result = await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidPrecondition);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_AFailedCondition_IsAPreconditionFailed()
    {
        var annotations = new RecordingAnnotations
        {
            Failure = new PreconditionFailedException("The annotation 'usg.northwind.invoicing' changed since it was read (If-Match); read it again and repeat the write.", ErrorCodes.AnnotationPreconditionFailed),
        };
        var request = Request("PUT", UsageBody);
        request.Headers.TryAddWithoutValidation("If-Match", StoredTag);

        var result = await Http(annotations).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        var answer = result.ShouldBeOfType<ObjectResult>();
        answer.StatusCode.ShouldBe(412);
        answer.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AnnotationPreconditionFailed);
    }

    [Fact]
    public void EntityTags_RoundTripAnyMarker()
    {
        foreach (var marker in new[] { StoredMarker, "\"1\"", "x", "\"ü/+=\"" })
        {
            var tag = AnnotationEntityTags.Create(marker);
            tag.IsWeak.ShouldBeFalse();
            AnnotationEntityTags.GetMarker(EntityTagHeaderValue.Parse(tag.ToString())).ShouldBe(marker);
        }
    }

    [Fact]
    public void EntityTags_OfAnyOrWeakTags_NameNoMarker()
    {
        AnnotationEntityTags.GetMarker(EntityTagHeaderValue.Any).ShouldBeNull();
        AnnotationEntityTags.GetMarker(EntityTagHeaderValue.Parse("W/" + StoredTag)).ShouldBeNull();
        AnnotationEntityTags.GetMarker(EntityTagHeaderValue.Parse("W/" + StoredTag), allowWeak: true).ShouldBe(StoredMarker);
        AnnotationEntityTags.ToHeader(null).ShouldBeNull();
        AnnotationEntityTags.ToHeader("absent").ShouldBeNull();
    }

    // The contract in the document: the ETag of the read and of the write, and the optional conditions of the write,
    // after its four path parameters, so a generated client takes them after the body it requires.
    [Fact]
    public async Task Document_AnnotationVersions_AreDeclared()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .ToList();
        var get = operations.Single(entry => entry.GetProperty("operationId").GetString() == Definitions.RouteIds.Annotations.GetAnnotation);
        var set = operations.Single(entry => entry.GetProperty("operationId").GetString() == Definitions.RouteIds.Annotations.SetAnnotation);

        foreach (var operation in new[] { get, set })
        {
            var tag = operation.GetProperty("responses").GetProperty("200").GetProperty("headers").GetProperty(HeaderNames.ETag);
            tag.GetProperty("description").GetString().ShouldBe(Definitions.Descriptions.AnnotationEntityTag);
            tag.GetProperty("schema").GetProperty("type").GetString().ShouldBe("string");
        }

        var parameters = set.GetProperty("parameters").EnumerateArray().ToList();
        parameters.Select(parameter => parameter.GetProperty("name").GetString()).ShouldBe(
            [Definitions.Parameters.Organization, Definitions.Parameters.Project, Definitions.Parameters.View, Definitions.Parameters.Key, Definitions.Parameters.IfMatch, Definitions.Parameters.IfNoneMatch]);

        foreach (var (parameter, description) in new[] { (parameters[4], Definitions.Descriptions.IfMatchAnnotation), (parameters[5], Definitions.Descriptions.IfNoneMatchAnnotation) })
        {
            parameter.GetProperty("in").GetString().ShouldBe("header");
            (parameter.TryGetProperty("required", out var required) && required.GetBoolean()).ShouldBeFalse();
            parameter.GetProperty("description").GetString().ShouldBe(description);
        }

        set.GetProperty("requestBody").GetProperty("required").GetBoolean().ShouldBeTrue();
        set.GetProperty("responses").GetProperty("412").GetProperty("description").GetString().ShouldBe(Definitions.Descriptions.ResponseAnnotationPreconditionFailed);
        get.TryGetProperty("parameters", out var getParameters).ShouldBeTrue();
        getParameters.EnumerateArray().ShouldNotContain(parameter => parameter.GetProperty("in").GetString() == "header");
    }

    private static TestHttpRequestData WithContext(TestHttpRequestData request)
    {
        return ConfigurationConditionalReadTests.WithHttpContext(request, ifNoneMatch: null);
    }
}
