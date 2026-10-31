using System.Diagnostics;
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

    // The annotation operations a machine needs Annotation.Manage for.
    public static TheoryData<string> ManagementOperations => new()
    {
        nameof(AnnotationsHttp.SetAnnotations),
        nameof(AnnotationsHttp.SetAnnotationsSimple),
        nameof(AnnotationsHttp.DeleteAnnotation),
        nameof(AnnotationsHttp.RestoreAnnotations),
    };

    // The catalog the name queries filter: two of each name, so every query has a match and a miss.
    private static readonly Annotation[] Catalog =
    [
        .. new[]
        {
            "rst.invoicing", "rst.shipping",
            "sbt.northwind", "sbt.contoso",
            "usg.northwind.invoicing", "usg.northwind.shipping", "usg.contoso.invoicing",
            "cnt.northwind.production", "cnt.northwind.staging", "cnt.contoso.production",
            "exe.northwind.invoicing.production", "exe.northwind.shipping.staging", "exe.contoso.invoicing.production",
            "unt.invoicing.api", "unt.shipping.worker",
            "uxe.northwind.invoicing.production.api", "uxe.northwind.shipping.staging.worker", "uxe.contoso.invoicing.production.api",
        }.Select(key => AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "billing", "default")),
    ];

    // Every name parameter of every typed list with an exact, a contains and a pattern query: the returned keys and
    // the partition an exact query narrows to.
    public static TheoryData<string, string, string, string[], string?> NameQueries => new()
    {
        { "responsibilities", "nameQuery", "invoicing", ["rst.invoicing"], "billing.rst.invoicing" },
        { "responsibilities", "nameQuery", "%ship%", ["rst.shipping"], null },
        { "responsibilities", "nameQuery", "^inv", ["rst.invoicing"], null },
        { "subjects", "nameQuery", "contoso", ["sbt.contoso"], "billing.sbt.contoso" },
        { "subjects", "nameQuery", "%wind", ["sbt.northwind"], null },
        { "subjects", "nameQuery", "^cont", ["sbt.contoso"], null },
        { "usages", "responsibilityNameQuery", "shipping", ["usg.northwind.shipping"], "billing.rst.shipping" },
        { "usages", "responsibilityNameQuery", "%voic%", ["usg.northwind.invoicing", "usg.contoso.invoicing"], null },
        { "usages", "responsibilityNameQuery", "^ship", ["usg.northwind.shipping"], null },
        { "usages", "subjectNameQuery", "contoso", ["usg.contoso.invoicing"], null },
        { "usages", "subjectNameQuery", "%wind", ["usg.northwind.invoicing", "usg.northwind.shipping"], null },
        { "usages", "subjectNameQuery", "^cont", ["usg.contoso.invoicing"], null },
        { "contexts", "subjectNameQuery", "northwind", ["cnt.northwind.production", "cnt.northwind.staging"], "billing.sbt.northwind" },
        { "contexts", "subjectNameQuery", "%oso", ["cnt.contoso.production"], null },
        { "contexts", "subjectNameQuery", "^north", ["cnt.northwind.production", "cnt.northwind.staging"], null },
        { "contexts", "nameQuery", "staging", ["cnt.northwind.staging"], null },
        { "contexts", "nameQuery", "%duct%", ["cnt.northwind.production", "cnt.contoso.production"], null },
        { "contexts", "nameQuery", "ing$", ["cnt.northwind.staging"], null },
        { "executions", "responsibilityNameQuery", "invoicing", ["exe.northwind.invoicing.production", "exe.contoso.invoicing.production"], "billing.rst.invoicing" },
        { "executions", "responsibilityNameQuery", "%ship%", ["exe.northwind.shipping.staging"], null },
        { "executions", "responsibilityNameQuery", "^inv", ["exe.northwind.invoicing.production", "exe.contoso.invoicing.production"], null },
        { "executions", "subjectNameQuery", "contoso", ["exe.contoso.invoicing.production"], null },
        { "executions", "subjectNameQuery", "%wind", ["exe.northwind.invoicing.production", "exe.northwind.shipping.staging"], null },
        { "executions", "subjectNameQuery", "^cont", ["exe.contoso.invoicing.production"], null },
        { "executions", "contextNameQuery", "staging", ["exe.northwind.shipping.staging"], null },
        { "executions", "contextNameQuery", "%duct%", ["exe.northwind.invoicing.production", "exe.contoso.invoicing.production"], null },
        { "executions", "contextNameQuery", "^stag", ["exe.northwind.shipping.staging"], null },
        { "units", "responsibilityNameQuery", "shipping", ["unt.shipping.worker"], "billing.rst.shipping" },
        { "units", "responsibilityNameQuery", "%voic%", ["unt.invoicing.api"], null },
        { "units", "responsibilityNameQuery", "^ship", ["unt.shipping.worker"], null },
        { "units-of-execution", "responsibilityNameQuery", "shipping", ["uxe.northwind.shipping.staging.worker"], "billing.rst.shipping" },
        { "units-of-execution", "responsibilityNameQuery", "%voic%", ["uxe.northwind.invoicing.production.api", "uxe.contoso.invoicing.production.api"], null },
        { "units-of-execution", "responsibilityNameQuery", "^ship", ["uxe.northwind.shipping.staging.worker"], null },
        { "units-of-execution", "unitNameQuery", "worker", ["uxe.northwind.shipping.staging.worker"], null },
        { "units-of-execution", "unitNameQuery", "%ap%", ["uxe.northwind.invoicing.production.api", "uxe.contoso.invoicing.production.api"], null },
        { "units-of-execution", "unitNameQuery", "^wor", ["uxe.northwind.shipping.staging.worker"], null },
        { "units-of-execution", "subjectNameQuery", "contoso", ["uxe.contoso.invoicing.production.api"], null },
        { "units-of-execution", "subjectNameQuery", "%wind", ["uxe.northwind.invoicing.production.api", "uxe.northwind.shipping.staging.worker"], null },
        { "units-of-execution", "subjectNameQuery", "^cont", ["uxe.contoso.invoicing.production.api"], null },
        { "units-of-execution", "contextNameQuery", "staging", ["uxe.northwind.shipping.staging.worker"], null },
        { "units-of-execution", "contextNameQuery", "%duct%", ["uxe.northwind.invoicing.production.api", "uxe.contoso.invoicing.production.api"], null },
        { "units-of-execution", "contextNameQuery", "^stag", ["uxe.northwind.shipping.staging.worker"], null },
    };

    // Every name parameter of every typed list.
    public static TheoryData<string, string> NameParameters => new()
    {
        { "responsibilities", "nameQuery" },
        { "subjects", "nameQuery" },
        { "usages", "responsibilityNameQuery" },
        { "usages", "subjectNameQuery" },
        { "contexts", "subjectNameQuery" },
        { "contexts", "nameQuery" },
        { "executions", "responsibilityNameQuery" },
        { "executions", "subjectNameQuery" },
        { "executions", "contextNameQuery" },
        { "units", "responsibilityNameQuery" },
        { "units-of-execution", "responsibilityNameQuery" },
        { "units-of-execution", "unitNameQuery" },
        { "units-of-execution", "subjectNameQuery" },
        { "units-of-execution", "contextNameQuery" },
    };

    // Debug builds (DEV_AUTH) skip the role check of users.
    private static bool DevAuth => Security.HttpRequestDataExtensions.IsDevelopmentAuthentication;

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
    public async Task SetMany_UnitWithATrigger_PassesTheConcreteUnit()
    {
        var annotations = new RecordingAnnotations();
        const string body = """
            [{
              "annotationKey": "unt.invoicing.export",
              "name": "export",
              "annotationType": "unit",
              "projectName": "x",
              "viewName": "y",
              "responsibilityKey": "rst.invoicing",
              "responsibilityName": "invoicing",
              "unitType": "time",
              "unitDefinition": "*/15 * * * *"
            }]
            """;

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        var created = annotations.LastCreated.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeOfType<Unit>();
        created.ProjectName.ShouldBe("billing");
        created.ViewName.ShouldBe("staging");
        created.UnitType.ShouldBe("time");
        created.UnitDefinition.ShouldBe("*/15 * * * *");
    }

    [Fact]
    public async Task SetMany_UnitWithoutATrigger_StaysABaseAnnotation()
    {
        var annotations = new RecordingAnnotations();
        const string body = """
            [{
              "annotationKey": "unt.invoicing.export",
              "name": "export",
              "annotationType": "Unit",
              "projectName": "x",
              "viewName": "y"
            }]
            """;

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        var created = annotations.LastCreated.ShouldNotBeNull().ShouldHaveSingleItem();
        created.ShouldBeOfType<Annotation>();
        created.ShouldNotBeOfType<Unit>();
        created.AnnotationType.ShouldBe(AnnotationType.Unit);
    }

    [Fact]
    public async Task SetMany_DuplicateType_UsesTheLastUnit()
    {
        var annotations = new RecordingAnnotations();
        const string body = """
            [{
              "annotationType": "subject",
              "annotationType": "unit",
              "annotationKey": "unt.invoicing.export",
              "name": "export",
              "projectName": "x",
              "viewName": "y",
              "responsibilityKey": "rst.invoicing",
              "responsibilityName": "invoicing",
              "unitType": "time",
              "unitDefinition": "*/15 * * * *"
            }]
            """;

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        result.ShouldBeOfType<OkObjectResult>();
        var created = annotations.LastCreated.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeOfType<Unit>();
        created.UnitType.ShouldBe("time");
        created.UnitDefinition.ShouldBe("*/15 * * * *");
    }

    [Fact]
    public async Task SetMany_UnitWithOnlyOneTriggerProperty_IsBadRequest()
    {
        var annotations = new RecordingAnnotations();
        const string body = """
            [{
              "annotationKey": "unt.invoicing.export",
              "name": "export",
              "annotationType": "Unit",
              "projectName": "x",
              "viewName": "y",
              "responsibilityKey": "rst.invoicing",
              "responsibilityName": "invoicing",
              "unitType": "message"
            }]
            """;

        var result = await Http(annotations).SetAnnotations(Request("POST", body), "acme", "billing", "staging");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldStartWith("Invalid annotation object in body:");
        annotations.Calls.ShouldBeEmpty();
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

    // A permanent delete is for users only: a machine is refused in every build, whatever its credentials carry,
    // User.Impersonation and every machine scope included.
    [Fact]
    public async Task Purge_ByMachine_IsDenied()
    {
        var annotations = new RecordingAnnotations();
        var request = TestRequests.MachineCarrying([Scopes.User.Impersonation, .. MachineScopes.ToScopeStrings(MachineScopes.All)], "DELETE");

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http(annotations, AccountRole.Administrator).PurgeAnnotation(request, "acme", "billing", "default", "sbt.a"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("A permanent delete needs a user with the Administrator role.");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Purge_ByAnAdministrator_CallsTheService()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations, AccountRole.Administrator).PurgeAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationPurgeResult>();
        annotations.Calls.ShouldBe(["purge:acme:sbt.a"]);
    }

    // Release builds check the role of users; Debug builds (DEV_AUTH) skip it.
    [Fact]
    public async Task Purge_ByAContributor_IsDeniedInRelease()
    {
        if (DevAuth)
        {
            return;
        }

        var annotations = new RecordingAnnotations();

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http(annotations).PurgeAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.a"));

        exception.Message.ShouldBe("No Administrator access to 'acme.billing'.");
        annotations.Calls.ShouldBeEmpty();
    }

    // Bulk create, soft delete (it cascades to the descendants) and restore need the management scope Annotation.Manage
    // from a machine (424-57), on top of the runtime scope Annotation.Write, checked with the machine document in every
    // build. Without it, the machine is refused (403 AccessDenied) before any service call; SetAnnotation, one
    // annotation at a time, stays a runtime write.
    [Theory]
    [MemberData(nameof(ManagementOperations))]
    public async Task ManagementOperation_ByAMachineWithTheRuntimeScopesOnly_IsAccessDeniedBeforeTheService(string operation)
    {
        var annotations = new RecordingAnnotations();

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CallManagement(MachineHttp(annotations, managementScopes: null), operation, MachineRequest(operation)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Annotation.Manage, which the machine was not created with.");
        annotations.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ManagementOperations))]
    public async Task ManagementOperation_ByAMachineWithAnnotationManage_ReachesTheService(string operation)
    {
        var annotations = new RecordingAnnotations();
        MachineScope[] manage = [MachineScope.AnnotationManage];

        var result = await CallManagement(MachineHttp(annotations, manage), operation, MachineRequest(operation, manage));

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldHaveSingleItem();
    }

    // The management scope grants nothing without the runtime scope of the operation, which is checked first.
    [Theory]
    [MemberData(nameof(ManagementOperations))]
    public async Task ManagementOperation_ByAMachineWithAnnotationManageButWithoutAnnotationWrite_IsAccessDeniedBeforeTheService(string operation)
    {
        var annotations = new RecordingAnnotations();
        var http = new AnnotationsHttp(
            annotations,
            new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead, MachineScope.AnnotationManage]),
            new FixedTime(Now),
            FunctionTestDoubles.ApiJsonOptions,
            NullLogger<AnnotationsHttp>.Instance);
        var request = KeyValueRequests.Machine(mtls: false, ManagementMethod(operation), body: ManagementBody(operation), roles: [Scopes.Annotation.Read, Scopes.Annotation.Manage]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CallManagement(http, operation, request));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Annotation.Write, which the machine was not created with.");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_ByAMachineWithTheRuntimeScopesOnly_StaysARuntimeWrite()
    {
        var annotations = new RecordingAnnotations();
        var request = KeyValueRequests.Machine(mtls: false, "PUT", body: SubjectJson, roles: [Scopes.Annotation.Read, Scopes.Annotation.Write]);

        var result = await MachineHttp(annotations, managementScopes: null).SetAnnotation(request, "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["set:acme:sbt.northwind"]);
    }

    // Users never need a management scope; their role decides, as before.
    [Theory]
    [MemberData(nameof(ManagementOperations))]
    public async Task ManagementOperation_ByAUser_NeedsNoManagementScope(string operation)
    {
        var annotations = new RecordingAnnotations();

        var result = await CallManagement(Http(annotations), operation, Request(ManagementMethod(operation), ManagementBody(operation)));

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task History_OfUnknownKey_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetAnnotationHistory(Request("GET"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<NotFoundResult>();
    }

    // 424-97: the name queries filter every typed list, and an exact one on a partition parameter narrows the read.
    [Theory]
    [MemberData(nameof(NameQueries))]
    public async Task TypedList_NameQuery_ReturnsTheMatchingAnnotations(string route, string parameter, string query, string[] keys, string? partitionKey)
    {
        var annotations = new RecordingAnnotations { Listed = Catalog };

        var result = await ListByName(Http(annotations), route, new Dictionary<string, string> { [parameter] = query });

        ListedKeys(result).ShouldBe(keys, ignoreOrder: true);
        annotations.LastRequest!.PartitionKey.ShouldBe(partitionKey);
    }

    // The responsibilities answered 500 with a name query: the conditions were cast to the entity type.
    [Theory]
    [InlineData("invoicing")]
    [InlineData("%voic%")]
    [InlineData("^inv")]
    public async Task Responsibilities_NameQuery_IsNoLongerAServerError(string query)
    {
        var annotations = new RecordingAnnotations { Listed = Catalog };

        var result = await Http(annotations).GetResponsibilities(Request("GET"), "acme", "billing", "default", nameQuery: query);

        ListedKeys(result).ShouldBe(["rst.invoicing"]);
        annotations.LastRequest!.Conditions.ShouldHaveSingleItem().Against.ShouldBe(AnnotationType.Responsibility);
    }

    // The name queries of one list combine with AND.
    [Theory]
    [InlineData("usages", "responsibilityNameQuery", "invoicing", "subjectNameQuery", "^cont", "usg.contoso.invoicing")]
    [InlineData("usages", "responsibilityNameQuery", "shipping", "subjectNameQuery", "contoso", null)]
    [InlineData("executions", "subjectNameQuery", "northwind", "contextNameQuery", "staging", "exe.northwind.shipping.staging")]
    [InlineData("executions", "responsibilityNameQuery", "%voic%", "contextNameQuery", "staging", null)]
    [InlineData("units-of-execution", "unitNameQuery", "api", "subjectNameQuery", "%wind", "uxe.northwind.invoicing.production.api")]
    [InlineData("units-of-execution", "unitNameQuery", "worker", "contextNameQuery", "^prod", null)]
    public async Task TypedList_TwoNameQueries_ReturnOnlyWhatMatchesBoth(string route, string first, string firstQuery, string second, string secondQuery, string? key)
    {
        var annotations = new RecordingAnnotations { Listed = Catalog };

        var result = await ListByName(Http(annotations), route, new Dictionary<string, string> { [first] = firstQuery, [second] = secondQuery });

        ListedKeys(result).ShouldBe(key is null ? [] : [key]);
    }

    [Theory]
    [MemberData(nameof(NameParameters))]
    public async Task TypedList_InvalidPattern_IsInvalidQueryBeforeTheRead(string route, string parameter)
    {
        var annotations = new RecordingAnnotations { Listed = Catalog };

        var exception = await Should.ThrowAsync<InvalidInputException>(() => ListByName(Http(annotations), route, new Dictionary<string, string> { [parameter] = "^[" }));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldStartWith($"The {parameter} is not a valid regular expression: ");
        annotations.LastRequest.ShouldBeNull();
    }

    // Nested repetitions backtrack for seconds per name on the backtracking engine; the name queries don't backtrack.
    [Fact]
    public async Task TypedList_CatastrophicPattern_AnswersWithoutDelay()
    {
        var annotations = new RecordingAnnotations
        {
            Listed = [.. Catalog, AnnotationFactory.CreateDefault(AnnotationKey.CreateResponsibility(new string('a', 30)), "billing", "default")],
        };
        var watch = Stopwatch.StartNew();

        var result = await Http(annotations).GetResponsibilities(Request("GET"), "acme", "billing", "default", nameQuery: "^([a-zA-Z0-9_-]+)+!");

        ListedKeys(result).ShouldBeEmpty();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    private static AnnotationsHttp Http(RecordingAnnotations annotations, AccountRole role = AccountRole.Contributor)
    {
        return new AnnotationsHttp(annotations, new UserAccess(role), new FixedTime(Now), FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);
    }

    // The machine of the project has the given management scopes.
    private static AnnotationsHttp MachineHttp(RecordingAnnotations annotations, IReadOnlyList<MachineScope>? managementScopes)
    {
        return new AnnotationsHttp(annotations, new RoleAccess(AccountRole.None, managementScopes: managementScopes), new FixedTime(Now), FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);
    }

    // A machine with the runtime scopes of annotations, and the given management scopes in its credentials.
    private static TestHttpRequestData MachineRequest(string operation, IReadOnlyList<MachineScope>? managementScopes = null)
    {
        return KeyValueRequests.Machine(mtls: false, ManagementMethod(operation), body: ManagementBody(operation), managementScopes: managementScopes, roles: [Scopes.Annotation.Read, Scopes.Annotation.Write]);
    }

    private static Task<IActionResult> CallManagement(AnnotationsHttp http, string operation, TestHttpRequestData request)
    {
        return operation switch
        {
            nameof(AnnotationsHttp.SetAnnotations) => http.SetAnnotations(request, "acme", "billing", "default"),
            nameof(AnnotationsHttp.SetAnnotationsSimple) => http.SetAnnotationsSimple(request, "acme", "billing", "default"),
            nameof(AnnotationsHttp.DeleteAnnotation) => http.DeleteAnnotation(request, "acme", "billing", "default", "sbt.northwind"),
            nameof(AnnotationsHttp.RestoreAnnotations) => http.RestoreAnnotations(request, "acme", "billing", "default"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static string ManagementMethod(string operation)
    {
        return operation == nameof(AnnotationsHttp.DeleteAnnotation) ? "DELETE" : "POST";
    }

    private static string? ManagementBody(string operation)
    {
        return operation switch
        {
            nameof(AnnotationsHttp.SetAnnotations) => $"[{SubjectJson}]",
            nameof(AnnotationsHttp.SetAnnotationsSimple) => FunctionTestDoubles.ToJsonBody<List<string>>(["sbt.northwind"]),
            nameof(AnnotationsHttp.RestoreAnnotations) => """{ "Keys": ["sbt.northwind"] }""",
            _ => null,
        };
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

    // A typed list called with the given name queries, by parameter name.
    private static Task<IActionResult> ListByName(AnnotationsHttp http, string route, IReadOnlyDictionary<string, string> queries)
    {
        var request = Request("GET");

        return route switch
        {
            "responsibilities" => http.GetResponsibilities(request, "acme", "billing", "default", nameQuery: Query("nameQuery")),
            "subjects" => http.GetSubjects(request, "acme", "billing", "default", nameQuery: Query("nameQuery")),
            "usages" => http.GetUsages(request, "acme", "billing", "default", responsibilityNameQuery: Query("responsibilityNameQuery"), subjectNameQuery: Query("subjectNameQuery")),
            "contexts" => http.GetContexts(request, "acme", "billing", "default", subjectNameQuery: Query("subjectNameQuery"), nameQuery: Query("nameQuery")),
            "executions" => http.GetExecutions(request, "acme", "billing", "default", responsibilityNameQuery: Query("responsibilityNameQuery"), subjectNameQuery: Query("subjectNameQuery"), contextNameQuery: Query("contextNameQuery")),
            "units" => http.GetUnits(request, "acme", "billing", "default", responsibilityNameQuery: Query("responsibilityNameQuery")),
            "units-of-execution" => http.GetUnitsOfExecution(request, "acme", "billing", "default", responsibilityNameQuery: Query("responsibilityNameQuery"), unitNameQuery: Query("unitNameQuery"), subjectNameQuery: Query("subjectNameQuery"), contextNameQuery: Query("contextNameQuery")),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
        };

        string? Query(string parameter) => queries.GetValueOrDefault(parameter);
    }

    // The keys of a typed list answer (AnnotationsResponse<T>).
    private static List<string> ListedKeys(IActionResult result)
    {
        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldNotBeNull();
        var annotations = (IEnumerable<Annotation>)response.GetType().GetProperty(nameof(AnnotationsResponse.Annotations))!.GetValue(response)!;
        return annotations.Select(annotation => annotation.AnnotationKey).ToList();
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
            new("scp", Scopes.User.Impersonation),
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

        // What GetAnnotationsAsync lists.
        public IReadOnlyList<Annotation> Listed { get; init; } = [];

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

        // The listed annotations of the requested types that pass the conditions of their type, as the Cosmos
        // service filters a page (it reads the partition first; the double ignores it).
        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
        {
            LastRequest = request;
            var annotations = Listed
                .Where(annotation => request.Types.Contains(annotation.AnnotationType))
                .Where(annotation => Passes(request, annotation))
                .ToList();
            return Task.FromResult(new AnnotationsResponse { Annotations = annotations, Count = annotations.Count });
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

        private static bool Passes(AnnotationsRequest request, Annotation annotation)
        {
            return annotation switch
            {
                Responsibility responsibility => request.CompileConditions<Responsibility>()(responsibility),
                Subject subject => request.CompileConditions<Subject>()(subject),
                Usage usage => request.CompileConditions<Usage>()(usage),
                Context context => request.CompileConditions<Context>()(context),
                Execution execution => request.CompileConditions<Execution>()(execution),
                Unit unit => request.CompileConditions<Unit>()(unit),
                UnitOfExecution unitOfExecution => request.CompileConditions<UnitOfExecution>()(unitOfExecution),
                _ => throw new ArgumentOutOfRangeException(nameof(annotation), annotation.AnnotationType, null),
            };
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // The signed-in user has the given role on every project.
    private sealed class UserAccess(AccountRole role) : IAccessService
    {
        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => Task.FromResult(role);

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
