using System.Reflection;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The restore routes of a version of a configuration, a template and a schema: what reaches the service, and how its
// outcomes and failures answer.
public class VersionRestoreHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "rst.billing";
    private const string Type = AnnotationTypeCodes.Execution;
    private const string DescendantType = AnnotationTypeCodes.Unit;

    private static readonly VersionRestoreResult Restored = VersionRestoreResult.Restored(3, 7, AuditStamp.Of(AuditActor.Account(TestRequests.UserId), DateTimeOffset.UnixEpoch));

    public static TheoryData<string, int, string> Failures()
    {
        return new TheoryData<string, int, string>
        {
            { "version", 404, ErrorCodes.VersionNotFound },
            { "annotation", 404, ErrorCodes.AnnotationNotFound },
            { "deleted", 409, ErrorCodes.AnnotationDeleted },
            { "conflict", 409, ErrorCodes.Conflict },
            { "invalid", 400, ErrorCodes.InvalidKey },
        };
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Configuration_PassesTheKeyTheVersionTheCallerAndForce(string? force, bool expected)
    {
        var (configurations, recorder) = Recording<IConfigurationService>();
        recorder.Returns(nameof(IConfigurationService.RestoreConfigurationVersionAsync), Restored);
        var request = Request(force);

        var result = await ConfigurationHttp(configurations).RestoreConfigurationVersion(request, Org, Project, View, Key, 3);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Restored);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IConfigurationService.RestoreConfigurationVersionAsync));
        call.Arg<FullKey>(0).ToString().ShouldBe(FullKey.Create(AnnotationKey.Parse(Key), Org, Project, View).ToString());
        call.Args[1].ShouldBe(3u);
        call.Args[2].ShouldBe(request.GetAuditActor());
        call.Args[3].ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Configuration_AnswersTheFailuresOfTheService(string failure, int status, string errorCode)
    {
        var configurations = Throwing<IConfigurationService>(Failure(failure));

        var result = await ConfigurationHttp(configurations).RestoreConfigurationVersion(Request(), Org, Project, View, Key, 3);

        ShouldAnswer(result, status, errorCode);
    }

    [Fact]
    public async Task Configuration_ASchemaViolation_ListsTheViolations()
    {
        var configurations = Throwing<IConfigurationService>(Violation());

        var result = await ConfigurationHttp(configurations).RestoreConfigurationVersion(Request(), Org, Project, View, Key, 3);

        var response = result.ShouldBeOfType<ConflictObjectResult>().Value.ShouldBeOfType<SchemaValidationErrorResponse>();
        response.Errors.ShouldHaveSingleItem().AnnotationKey.ShouldBe(Key);
    }

    [Fact]
    public async Task Configuration_AnInvalidKey_IsABadRequestBeforeTheService()
    {
        var result = await ConfigurationHttp(Untouched<IConfigurationService>()).RestoreConfigurationVersion(Request(), Org, Project, View, "billing", 3);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Template_PassesTheTypeTheVersionAndTheCaller()
    {
        var (templates, recorder) = Recording<IConfigurationTemplateService>();
        recorder.Returns(nameof(IConfigurationTemplateService.RestoreTemplateVersionAsync), Restored);
        var request = Request();

        var result = await TemplateHttp(templates).RestoreTemplateVersion(request, Org, Project, View, Type, 3);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Restored);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Args.Take(5).ShouldBe(new object[] { Org, Project, View, Type, 3u });
        call.Args[5].ShouldBe(request.GetAuditActor());
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Template_AnswersTheFailuresOfTheService(string failure, int status, string errorCode)
    {
        var result = await TemplateHttp(Throwing<IConfigurationTemplateService>(Failure(failure))).RestoreTemplateVersion(Request(), Org, Project, View, Type, 3);

        ShouldAnswer(result, status, errorCode);
    }

    [Fact]
    public async Task Template_AConcurrentChange_IsAConflict()
    {
        var templates = Throwing<IConfigurationTemplateService>(new TemplateConcurrencyException(Project, View, Type, 3));

        var result = await TemplateHttp(templates).RestoreTemplateVersion(Request(), Org, Project, View, Type, 3);

        result.ShouldBeOfType<ConflictObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Template_AnUnknownTypeCode_IsABadRequestBeforeTheService()
    {
        var result = await TemplateHttp(Untouched<IConfigurationTemplateService>()).RestoreTemplateVersion(Request(), Org, Project, View, "xyz", 3);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("type")]
    [InlineData("annotation")]
    [InlineData("descendant")]
    public async Task Schema_PassesItsDocumentTheVersionTheCallerAndForce(string kind)
    {
        var (schemas, recorder) = Recording<IConfigurationSchemaService>();
        var request = Request("true");

        var (method, expected) = kind switch
        {
            "type" => (nameof(IConfigurationSchemaService.RestoreSchemaVersionAsync), new object[] { Org, Project, View, Type, 3u }),
            "annotation" => (nameof(IConfigurationSchemaService.RestoreAnnotationSchemaVersionAsync), new object[] { Org, Project, View, Key, 3u }),
            _ => (nameof(IConfigurationSchemaService.RestoreDescendantTypeSchemaVersionAsync), new object[] { Org, Project, View, Key, DescendantType, 3u }),
        };
        recorder.Returns(method, Restored);

        var result = await RestoreSchema(SchemaHttp(schemas), kind, request);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Restored);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(method);
        call.Args.Take(expected.Length).ShouldBe(expected);
        call.Args[expected.Length].ShouldBe(request.GetAuditActor());
        call.Args[expected.Length + 1].ShouldBe(true);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Schema_AnswersTheFailuresOfTheService(string failure, int status, string errorCode)
    {
        foreach (var kind in new[] { "type", "annotation", "descendant" })
        {
            var result = await RestoreSchema(SchemaHttp(Throwing<IConfigurationSchemaService>(Failure(failure))), kind, Request());

            ShouldAnswer(result, status, errorCode);
        }
    }

    [Fact]
    public async Task Schema_ViolationsAndConcurrentChanges_AreConflicts()
    {
        var violation = await RestoreSchema(SchemaHttp(Throwing<IConfigurationSchemaService>(Violation())), "type", Request());
        var concurrent = await RestoreSchema(SchemaHttp(Throwing<IConfigurationSchemaService>(new SchemaConcurrencyException(Project, View, "default.cfs.t.exe", 3))), "annotation", Request());

        violation.ShouldBeOfType<ConflictObjectResult>().Value.ShouldBeOfType<SchemaValidationErrorResponse>().Errors.ShouldHaveSingleItem().AnnotationKey.ShouldBe(Key);
        concurrent.ShouldBeOfType<ConflictObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Schema_AnInvalidKeyOrType_IsABadRequestBeforeTheService()
    {
        var http = SchemaHttp(Untouched<IConfigurationSchemaService>());

        (await http.RestoreSchemaVersion(Request(), Org, Project, View, "xyz", 3)).ShouldBeOfType<BadRequestObjectResult>();
        (await http.RestoreAnnotationSchemaVersion(Request(), Org, Project, View, "billing", 3)).ShouldBeOfType<BadRequestObjectResult>();
        (await http.RestoreDescendantTypeSchemaVersion(Request(), Org, Project, View, Key, "xyz", 3)).ShouldBeOfType<BadRequestObjectResult>();
    }

    private static Task<IActionResult> RestoreSchema(ConfigurationSchemaHttp http, string kind, TestHttpRequestData request)
    {
        return kind switch
        {
            "type" => http.RestoreSchemaVersion(request, Org, Project, View, Type, 3),
            "annotation" => http.RestoreAnnotationSchemaVersion(request, Org, Project, View, Key, 3),
            _ => http.RestoreDescendantTypeSchemaVersion(request, Org, Project, View, Key, DescendantType, 3),
        };
    }

    private static void ShouldAnswer(IActionResult result, int status, string errorCode)
    {
        var answer = result.ShouldBeOfType<ObjectResult>();
        answer.StatusCode.ShouldBe(status);
        answer.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(errorCode);
    }

    private static Exception Failure(string failure)
    {
        return failure switch
        {
            "version" => new NotFoundException("The configuration of 'rst.billing' has no version 3.", ErrorCodes.VersionNotFound),
            "annotation" => new NotFoundException("The annotation 'rst.billing' does not exist.", ErrorCodes.AnnotationNotFound),
            "deleted" => new ConflictException("The annotation 'rst.billing' is deleted.", ErrorCodes.AnnotationDeleted),
            "conflict" => new ConflictException("The configuration 'rst.billing' was changed concurrently, try again."),
            _ => new InvalidInputException("The type sbt cannot descend from rst.billing.", ErrorCodes.InvalidKey),
        };
    }

    private static SchemaValidationException Violation()
    {
        return new SchemaValidationException([new SchemaValidationError { AnnotationKey = Key, ViewName = View, Errors = ["#: Required properties are missing: [b]"] }]);
    }

    private static TestHttpRequestData Request(string? force = null)
    {
        return KeyValueRequests.User("POST", force is null ? null : query => query[Definitions.Parameters.Force] = force);
    }

    private static ConfigurationHttp ConfigurationHttp(IConfigurationService configurations)
    {
        return new ConfigurationHttp(configurations, Untouched<IAnnotationService>(), new RoleAccess(AccountRole.Contributor), new FixedKeyValuePolicies(requireMtls: false), NullLogger<ConfigurationHttp>.Instance);
    }

    private static TemplateHttp TemplateHttp(IConfigurationTemplateService templates)
    {
        return new TemplateHttp(templates, new RoleAccess(AccountRole.Contributor), NullLogger<TemplateHttp>.Instance);
    }

    private static ConfigurationSchemaHttp SchemaHttp(IConfigurationSchemaService schemas)
    {
        return new ConfigurationSchemaHttp(schemas, new RoleAccess(AccountRole.Contributor), NullLogger<ConfigurationSchemaHttp>.Instance);
    }

    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, MachineRestrictionHttpTests.UntouchedProxy>();
    }

    private static (T Service, MachineRestrictionHttpTests.RecordingProxy Recorder) Recording<T>()
        where T : class
    {
        var service = DispatchProxy.Create<T, MachineRestrictionHttpTests.RecordingProxy>();
        return (service, (MachineRestrictionHttpTests.RecordingProxy)(object)service);
    }

    private static T Throwing<T>(Exception exception)
        where T : class
    {
        var service = DispatchProxy.Create<T, ThrowingProxy>();
        ((ThrowingProxy)(object)service).Exception = exception;
        return service;
    }

    // Every call throws the given exception, as a service that fails.
    public class ThrowingProxy : DispatchProxy
    {
        public Exception? Exception { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw Exception ?? new InvalidOperationException("No exception to throw.");
        }
    }
}
