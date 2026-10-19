using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Transferring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class AnnotationsHttpTests
{
    private const string UsageBody = """
        {"AnnotationKey":"usg.northwind.invoicing","Name":"invoicing","AnnotationType":"Usage","ProjectName":"x","ViewName":"y",
         "SubjectKey":"sbt.northwind","ResponsibilityKey":"rst.invoicing","SubjectName":"northwind","ResponsibilityName":"invoicing",
         "Title":"Invoices of Northwind"}
        """;

    // One annotation of a bulk create, with every required member.
    private const string SubjectJson = """{"AnnotationKey":"sbt.northwind","Name":"northwind","AnnotationType":"Subject","ProjectName":"x","ViewName":"y"}""";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Set_StoresTheTypedAnnotationUnderTheRouteAndReturnsTheStoredOne(string method)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotation(Request(method, UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        var stored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Usage>();
        stored.ProjectName.ShouldBe("billing");
        stored.ViewName.ShouldBe("default");
        stored.Title.ShouldBe("Invoices of Northwind");
        stored.SubjectKey.ShouldBe("sbt.northwind");
        stored.Created.ShouldNotBeNull("the stored annotation is returned, not the body");
        annotations.Calls.ShouldBe(["set:acme:usg.northwind.invoicing"]);
    }

    [Fact]
    public async Task Set_InvalidContent_AnswersTheDomainError()
    {
        var annotations = new RecordingAnnotations { Failure = new InvalidInputException("Title is too long.", ErrorCodes.InvalidAnnotation) };

        var result = await Http(annotations).SetAnnotation(Request("POST", UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe(400);
        error.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
    }

    [Fact]
    public async Task Set_InvalidKey_IsRejectedWithoutCallingTheService()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotation(Request("POST", UsageBody), "acme", "billing", "default", "nope");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_BodyWithoutTheTypeSpecificMembers_IsRejected()
    {
        var annotations = new RecordingAnnotations();
        const string baseOnly = """{"AnnotationKey":"usg.northwind.invoicing","Name":"invoicing","AnnotationType":"Usage","ProjectName":"x","ViewName":"y"}""";

        var result = await Http(annotations).SetAnnotation(Request("POST", baseOnly), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("SubjectKey");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_UnknownType_IsRejected()
    {
        var result = await Http(new RecordingAnnotations()).SetAnnotation(Request("POST", """{"AnnotationKey":"x.y","AnnotationType":"Thing"}"""), "acme", "billing", "default", "x.y");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("AnnotationType");
    }

    // TryGetProperty throws for a root that is not an object, which answered 500.
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("\"usg.northwind.invoicing\"")]
    public async Task Set_BodyThatIsNotAnObject_IsRejected(string body)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotation(Request("POST", body), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldBe("No annotation object in body.");
        annotations.Calls.ShouldBeEmpty();
    }

    // Decoding to a string first would store a replacement character the client never sent.
    [Fact]
    public async Task Set_BodyThatIsNotUtf8_IsRejected()
    {
        var annotations = new RecordingAnnotations();

        // The title "Café" in Latin-1.
        var json = UsageBody.Replace("Invoices of Northwind", "Caf", StringComparison.Ordinal);
        var at = json.IndexOf("\"Caf\"", StringComparison.Ordinal) + 4;
        byte[] body = [.. Encoding.UTF8.GetBytes(json[..at]), 0xE9, .. Encoding.UTF8.GetBytes(json[at..])];

        var result = await Http(annotations).SetAnnotation(Request("POST", body), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>();
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_WithAByteOrderMark_IsStored()
    {
        var annotations = new RecordingAnnotations();
        byte[] body = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(UsageBody)];

        var result = await Http(annotations).SetAnnotation(Request("POST", body), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["set:acme:usg.northwind.invoicing"]);
    }

    [Fact]
    public async Task Set_LargerThanItsLimit_IsTooLargeWithoutCallingTheService()
    {
        var annotations = new RecordingAnnotations();
        var body = Encoding.UTF8.GetBytes(UsageBody.PadRight(RequestBodyLimits.Annotation + 1));

        var result = await Http(annotations).SetAnnotation(Request("POST", body), "acme", "billing", "default", "usg.northwind.invoicing");

        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
        error.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.BodyTooLarge);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_DeclaredLengthOverItsLimit_IsTooLarge()
    {
        var headers = FunctionTestDoubles.JsonContentType();
        headers["Content-Length"] = (RequestBodyLimits.Annotation + 1).ToString(CultureInfo.InvariantCulture);
        var request = Request("POST", Encoding.UTF8.GetBytes(UsageBody), headers);

        var result = await Http(new RecordingAnnotations()).SetAnnotation(request, "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
        request.Body.Position.ShouldBe(0);
    }

    [Fact]
    public async Task SetSimple_CreatesInTheProjectAndViewOfTheRoute()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotationsSimple(Request("POST", FunctionTestDoubles.ToJsonBody<List<string>>(["northwind.invoicing"])), "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["simple:acme:billing:staging:northwind.invoicing"]);
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran.
    [Theory]
    [InlineData("", "The annotations are missing.")]
    [InlineData("null", "The annotations are missing.")]
    [InlineData("{}", "The request body is not a list of annotation keys or lines (at $).")]
    public async Task SetSimple_MissingOrInvalidBody_IsBadRequestWithoutCallingTheService(string body, string message)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotationsSimple(Request("POST", body), "acme", "billing", "staging");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetMany_CreatesInTheProjectAndViewOfTheRoute()
    {
        var annotations = new RecordingAnnotations();
        var body = FunctionTestDoubles.ToJsonBody<List<Annotation>>([Subject("sbt.northwind"), Subject("sbt.contoso")]);

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        var created = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<Annotation>>()!;
        created.Select(annotation => annotation.AnnotationKey).ShouldBe(["sbt.northwind", "sbt.contoso"]);
        annotations.Calls.ShouldBe(["create:acme:sbt.northwind,sbt.contoso"]);
        annotations.LastCreated.ShouldNotBeNull().ShouldAllBe(annotation => annotation.ProjectName == "billing" && annotation.ViewName == "staging");
    }

    // The worker's [FromBody] binding failed the first three with a 500 before the handler ran, and a null entry
    // failed the handler itself.
    [Theory]
    [InlineData("", "The annotations are missing.")]
    [InlineData("null", "The annotations are missing.")]
    [InlineData("{}", "The request body is not a list of annotations (at $).")]
    [InlineData("[null]", "The annotations must not contain null entries.")]
    [InlineData("[" + SubjectJson + ", null]", "The annotations must not contain null entries.")]
    public async Task SetMany_MissingOrInvalidBody_IsBadRequestWithoutCallingTheService(string body, string message)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetManyAndSimple_EmptyList_AnswerNoAnnotations()
    {
        var http = Http(new RecordingAnnotations());

        var many = await http.SetAnnotations(Request("POST", "[]"), "acme", "billing", "staging");
        var simple = await http.SetAnnotationsSimple(Request("POST", "[]"), "acme", "billing", "staging");

        many.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<Annotation>>()!.ShouldBeEmpty();
        simple.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<Annotation>>()!.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task SetMany_BodyWithoutAContentType_IsRead()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("POST", $"[{SubjectJson}]");
        request.Headers.Remove("Content-Type").ShouldBeTrue();

        var result = await Http(annotations).SetAnnotations(request, "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["create:acme:sbt.northwind"]);
    }

    [Fact]
    public async Task SetMany_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var annotations = new RecordingAnnotations();
        const string body = """[{ "annotationKey": "usg.northwind.invoicing", "NAME": "invoicing", "annotationType": "usage", "projectName": "x", "viewName": "y", }]""";

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        var created = annotations.LastCreated.ShouldNotBeNull().ShouldHaveSingleItem();
        created.AnnotationKey.ShouldBe("usg.northwind.invoicing");
        created.AnnotationType.ShouldBe(AnnotationType.Usage);
    }

    [Fact]
    public async Task Delete_ReturnsTheDeletion()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).DeleteAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.northwind");

        var deletion = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationDeletionResult>();
        deletion.Deletion.RootKey.ShouldBe("sbt.northwind");
        annotations.Calls.ShouldBe(["delete:acme:sbt.northwind"]);
    }

    [Fact]
    public async Task Delete_UnknownKey_Answers404()
    {
        var annotations = new RecordingAnnotations { Failure = new NotFoundException("missing") };

        var result = await Http(annotations).DeleteAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Descendants_AskTheServiceForTheGraphOfTheKey()
    {
        var annotations = new RecordingAnnotations();

        await Http(annotations).GetDescendants(Request("GET"), "acme", "billing", "default", "cnt.northwind.prod", "all");

        var request = annotations.LastRequest.ShouldNotBeNull();
        request.DescendantsOf!.ToString().ShouldBe("cnt.northwind.prod");
        request.Conditions.ShouldBeEmpty();
        request.PartitionKey.ShouldBeNull();
    }

    [Fact]
    public async Task Lists_ByDefault_AskOnlyForActiveAnnotations()
    {
        var annotations = new RecordingAnnotations();
        var http = Http(annotations);

        foreach (var list in Lists(http))
        {
            await list(false, false);

            var request = annotations.LastRequest.ShouldNotBeNull();
            request.IncludeInactive.ShouldBeFalse();
            request.Deleted.ShouldBe(AnnotationDeletedFilter.Exclude);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Lists_PassTheIncludeFlags(bool includeInactive, bool includeDeleted)
    {
        var annotations = new RecordingAnnotations();
        var http = Http(annotations);

        foreach (var list in Lists(http))
        {
            await list(includeInactive, includeDeleted);

            var request = annotations.LastRequest.ShouldNotBeNull();
            request.IncludeInactive.ShouldBe(includeInactive);
            request.Deleted.ShouldBe(includeDeleted ? AnnotationDeletedFilter.Include : AnnotationDeletedFilter.Exclude);
        }
    }

    [Fact]
    public async Task Get_ActiveAnnotation_IsReturned()
    {
        var annotations = new RecordingAnnotations { Stored = Subject("sbt.northwind") };

        var result = await Http(annotations).GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Subject>().AnnotationKey.ShouldBe("sbt.northwind");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("scheduled")]
    [InlineData("expired")]
    public async Task Get_InactiveAnnotation_Answers404AnnotationInactive(string status)
    {
        var annotations = new RecordingAnnotations { Stored = Inactive(Subject("sbt.northwind"), status) };

        var result = await Http(annotations).GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind");

        var error = result.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.AnnotationInactive);
        error.Message.ShouldContain(status);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("scheduled")]
    [InlineData("expired")]
    public async Task Get_InactiveAnnotationWithIncludeInactive_IsReturned(string status)
    {
        var annotations = new RecordingAnnotations { Stored = Inactive(Subject("sbt.northwind"), status) };

        var result = await Http(annotations).GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind", includeInactive: true);

        result.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_DeletedAnnotation_Answers404UnlessIncluded()
    {
        var annotations = new RecordingAnnotations { StoredDeleted = Deleted(Subject("sbt.northwind")) };
        var http = Http(annotations);

        var hidden = await http.GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind");
        var included = await http.GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind", includeDeleted: true);

        hidden.ShouldBeOfType<NotFoundResult>();
        included.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Subject>().Deletion.ShouldNotBeNull();
    }

    [Fact]
    public async Task Get_DeletedAndDisabledAnnotation_NeedsBothFlags()
    {
        var annotations = new RecordingAnnotations { StoredDeleted = Deleted(Inactive(Subject("sbt.northwind"), "disabled")) };
        var http = Http(annotations);

        var deletedOnly = await http.GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind", includeDeleted: true);
        var both = await http.GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind", includeInactive: true, includeDeleted: true);

        deletedOnly.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AnnotationInactive);
        both.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_UnknownKey_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind", includeInactive: true, includeDeleted: true);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeletedList_PassesTheFilters()
    {
        var annotations = new RecordingAnnotations();

        await Http(annotations).GetDeletedAnnotations(Request("GET"), "acme", "billing", "default", roots: false, deletionId: "d1", continuationToken: "t1");

        annotations.Calls.ShouldBe(["deleted:acme:billing:default:False:d1:t1"]);
    }

    [Fact]
    public async Task DeletedAnnotation_NotDeleted_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetDeletedAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Lookup_WithoutKeysOrWithTooMany_IsRejected(int count)
    {
        var annotations = new RecordingAnnotations();
        var keys = Enumerable.Range(0, count).Select(index => $"sbt.s{index}").ToList();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationKeysRequest { Keys = keys })), "acme", "billing", "default");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lookup_WithInvalidKeys_ListsThem()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationKeysRequest { Keys = ["sbt.ok", "nope", "xyz.a"] })), "acme", "billing", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        error.Message.ShouldContain("nope");
        error.Message.ShouldContain("xyz.a");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lookup_PassesTheParsedKeys()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationKeysRequest { Keys = ["sbt.a", "usg.a.b"] })), "acme", "billing", "default");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["find:acme:billing:default:sbt.a,usg.a.b"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_PassesTheKeysTheDescendantsFlagAndTheCaller(bool descendants)
    {
        var annotations = new RecordingAnnotations();
        var request = Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationRestoreRequest { Keys = ["sbt.a"], Descendants = descendants }));

        var result = await Http(annotations).RestoreAnnotations(request, "acme", "billing", "default");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationRestoreResult>();
        annotations.Calls.ShouldBe([$"restore:acme:billing:default:sbt.a:{descendants}"]);
        annotations.LastActor.ShouldBe(request.GetAuditActor());
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The keys are required,
    // so {} is not a request either.
    [Theory]
    [InlineData("", "No annotation keys in the body.")]
    [InlineData("null", "No annotation keys in the body.")]
    [InlineData("{}", "The request body is not an annotation lookup request (at $).")]
    [InlineData("[1, 2]", "The request body is not an annotation lookup request (at $).")]
    public async Task Lookup_MissingOrInvalidBody_IsRejected(string body, string message)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST", body), "acme", "billing", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "No annotation keys in the body.")]
    [InlineData("null", "No annotation keys in the body.")]
    [InlineData("{}", "The request body is not an annotation restore request (at $).")]
    [InlineData("[1, 2]", "The request body is not an annotation restore request (at $).")]
    public async Task Restore_MissingOrInvalidBody_IsRejected(string body, string message)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).RestoreAnnotations(Request("POST", body), "acme", "billing", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Restore_PropertyNamesInAnyCase_AreRead()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).RestoreAnnotations(Request("POST", """{ "keys": ["sbt.a"], "DESCENDANTS": false, }"""), "acme", "billing", "default");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["restore:acme:billing:default:sbt.a:False"]);
    }

    [Fact]
    public async Task Purge_ByMachine_IsDenied()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("DELETE");
        request.FunctionContext.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

        await Should.ThrowAsync<AccessDeniedException>(() => Http(annotations).PurgeAnnotation(request, "acme", "billing", "default", "sbt.a"));
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Purge_ByUser_CallsTheService()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).PurgeAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationPurgeResult>();
        annotations.Calls.ShouldBe(["purge:acme:sbt.a"]);
    }

    [Fact]
    public async Task History_OfUnknownKey_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetAnnotationHistory(Request("GET"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<NotFoundResult>();
    }

    private static AnnotationsHttp Http(RecordingAnnotations annotations)
    {
        return new AnnotationsHttp(annotations, new ContributorAccess(), new FixedTime(Now), FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);
    }

    // Every list endpoint and the descendants, called with the two include flags.
    private static IEnumerable<Func<bool, bool, Task>> Lists(AnnotationsHttp http)
    {
        yield return (inactive, deleted) => http.GetAnnotations(Request("GET"), "acme", "billing", "default", null, inactive, deleted);
        yield return (inactive, deleted) => http.GetResponsibilities(Request("GET"), "acme", "billing", "default", null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetSubjects(Request("GET"), "acme", "billing", "default", null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetUsages(Request("GET"), "acme", "billing", "default", null, null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetContexts(Request("GET"), "acme", "billing", "default", null, null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetExecutions(Request("GET"), "acme", "billing", "default", null, null, null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetUnits(Request("GET"), "acme", "billing", "default", null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetUnitsOfExecution(Request("GET"), "acme", "billing", "default", null, null, null, null, null, inactive, deleted);
        yield return (inactive, deleted) => http.GetDescendants(Request("GET"), "acme", "billing", "default", "sbt.northwind", "all", null, inactive, deleted);
    }

    private static Subject Subject(string key)
    {
        return new Subject
        {
            ProjectName = "billing",
            ViewName = "default",
            AnnotationKey = key,
            Name = AnnotationKey.Parse(key).Name,
            AnnotationType = AnnotationType.Subject,
        };
    }

    private static Subject Inactive(Subject subject, string status)
    {
        return status switch
        {
            "disabled" => subject with { IsDisabled = true },
            "scheduled" => subject with { ValidFrom = Now.AddDays(1) },
            "expired" => subject with { ExpiresAt = Now },
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    private static Subject Deleted(Subject subject)
    {
        return subject with
        {
            Deletion = new AnnotationDeletion { DeletionId = "d1", RootKey = subject.AnnotationKey, DeletedAt = DateTimeOffset.UnixEpoch, DeletedBy = AuditActor.System, IsRoot = true },
        };
    }

    // A body goes as JSON, with its content type.
    private static TestHttpRequestData Request(string method, string? body = null)
    {
        return Request(method, body is null ? null : Encoding.UTF8.GetBytes(body));
    }

    private static TestHttpRequestData Request(string method, byte[]? body, Dictionary<string, string>? headers = null)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers ?? (body is null ? null : FunctionTestDoubles.JsonContentType()), method, bodyBytes: body);
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user_01"),
            new("scp", $"{Scopes.Annotation.Read} {Scopes.Annotation.Write}"),
        };
        return request;
    }

    private sealed class RecordingAnnotations : IAnnotationService
    {
        private static readonly AuditStamp Stamp = new() { At = DateTimeOffset.UnixEpoch, By = AuditActor.System };

        public List<string> Calls { get; } = [];

        public Exception? Failure { get; init; }

        public AnnotationsRequest? LastRequest { get; private set; }

        public AuditActor? LastActor { get; private set; }

        // The annotations CreateAnnotationsAsync was given.
        public IReadOnlyList<Annotation>? LastCreated { get; private set; }

        // What GetAnnotationAsync and GetDeletedAnnotationAsync find.
        public Annotation? Stored { get; init; }

        public Annotation? StoredDeleted { get; init; }

        public Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor)
        {
            Calls.Add($"set:{organization}:{annotation.AnnotationKey}");
            LastActor = actor;
            return Failure is null
                ? Task.FromResult(annotation with { Created = Stamp, Modified = Stamp })
                : Task.FromException<Annotation>(Failure);
        }

        public Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor)
        {
            if (Failure is not null)
            {
                return Task.FromException<AnnotationDeletionResult>(Failure);
            }

            Calls.Add($"delete:{fullKey.OrganizationName}:{fullKey.Annotation}");
            return Task.FromResult(new AnnotationDeletionResult
            {
                Deletion = new AnnotationDeletion { DeletionId = "d1", RootKey = fullKey.Annotation, DeletedAt = DateTimeOffset.UnixEpoch, DeletedBy = actor, IsRoot = true },
                DeletedKeys = [fullKey.Annotation],
            });
        }

        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
        {
            LastRequest = request;
            return Task.FromResult(new AnnotationsResponse());
        }

        public Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(string organization, string project, string view, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnotationsResponse> GetDeletedAnnotationsAsync(string organization, string project, string view, bool rootsOnly = true, string? deletionId = null, string? continuationToken = null)
        {
            Calls.Add($"deleted:{organization}:{project}:{view}:{rootsOnly}:{deletionId}:{continuationToken}");
            return Task.FromResult(new AnnotationsResponse());
        }

        public Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys)
        {
            Calls.Add($"find:{organization}:{project}:{view}:{string.Join(",", keys.Select(key => key.ToString()))}");
            return Task.FromResult<IReadOnlyList<Annotation>>([]);
        }

        public Task<AnnotationRestoreResult> RestoreAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, AuditActor actor, bool includeDescendants = true)
        {
            Calls.Add($"restore:{organization}:{project}:{view}:{string.Join(",", keys.Select(key => key.ToString()))}:{includeDescendants}");
            LastActor = actor;
            return Task.FromResult(new AnnotationRestoreResult { Annotations = [] });
        }

        public Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor)
        {
            Calls.Add($"purge:{fullKey.OrganizationName}:{fullKey.Annotation}");
            return Task.FromResult(new AnnotationPurgeResult { PurgedKeys = [fullKey.Annotation] });
        }

        public Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey) => Task.FromResult(StoredDeleted);

        public Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey) => Task.FromResult<AnnotationHistory?>(null);

        public Task<bool> ExistAnnotationAsync(FullKey fullKey) => throw new NotImplementedException();

        public Task<Annotation?> GetAnnotationAsync(FullKey fullKey) => Task.FromResult(Stored);

        public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotImplementedException();

        public Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor)
        {
            var created = annotations.ToList();
            Calls.Add($"create:{organization}:{string.Join(",", created.Select(annotation => annotation.AnnotationKey))}");
            LastCreated = created;
            LastActor = actor;
            return Task.FromResult<IEnumerable<Annotation>>(created.Select(annotation => annotation with { Created = Stamp, Modified = Stamp }).ToList());
        }

        public Task<IReadOnlySet<string>> FindActiveAnnotationKeysAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, string project, string view, IEnumerable<string> annotations, AuditActor actor)
        {
            Calls.Add($"simple:{organization}:{project}:{view}:{string.Join(",", annotations)}");
            return Task.FromResult<IEnumerable<Annotation>>([]);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // The signed-in user is a Contributor of every project.
    private sealed class ContributorAccess : IAccessService
    {
        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => Task.FromResult(AccountRole.Contributor);

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}
