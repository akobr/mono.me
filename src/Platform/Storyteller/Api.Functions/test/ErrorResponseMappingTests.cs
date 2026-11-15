using System.Net;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ErrorResponseMappingTests
{
    public static TheoryData<Exception, HttpStatusCode, string> ClientErrors => new()
    {
        { new AccessDeniedException("no role"), HttpStatusCode.Forbidden, ErrorCodes.AccessDenied },
        { new NotFoundException("missing"), HttpStatusCode.NotFound, ErrorCodes.NotFound },
        { new ConflictException("duplicate"), HttpStatusCode.Conflict, ErrorCodes.Conflict },
        { new ConflictException("last owner", ErrorCodes.LastOwner), HttpStatusCode.Conflict, ErrorCodes.LastOwner },
        { new InvalidInputException("bad name", ErrorCodes.InvalidName), HttpStatusCode.BadRequest, ErrorCodes.InvalidName },
        { new InvalidInputException("title too long", ErrorCodes.InvalidAnnotation), HttpStatusCode.BadRequest, ErrorCodes.InvalidAnnotation },
        { new InvalidInputException("bad key", ErrorCodes.InvalidKey), HttpStatusCode.BadRequest, ErrorCodes.InvalidKey },
        { new InvalidInputException("bad pattern", ErrorCodes.InvalidQuery), HttpStatusCode.BadRequest, ErrorCodes.InvalidQuery },
        { new MachineAccessNotSupportedException("no key to reset"), HttpStatusCode.BadRequest, ErrorCodes.MachineAccessNotSupported },
        { new FeatureNotConfiguredException("no provider", ErrorCodes.MachineAccessNotConfigured), HttpStatusCode.NotImplemented, ErrorCodes.MachineAccessNotConfigured },
        { new ConflictException("deleted", ErrorCodes.AnnotationDeleted), HttpStatusCode.Conflict, ErrorCodes.AnnotationDeleted },
        { new ConflictException("active", ErrorCodes.AnnotationNotDeleted), HttpStatusCode.Conflict, ErrorCodes.AnnotationNotDeleted },
        { new JsonPatchException("stale", JsonPatchFailureKind.TestFailed, 0), HttpStatusCode.PreconditionFailed, ErrorCodes.PatchTestFailed },
        { new JsonPatchException("unknown op", JsonPatchFailureKind.Invalid), HttpStatusCode.BadRequest, ErrorCodes.PatchInvalid },
        { new JsonPatchException("no path", JsonPatchFailureKind.OperationFailed, 1), HttpStatusCode.BadRequest, ErrorCodes.PatchInvalid },
        { new ProjectSettingsInvalidException([new ProjectSettingsProblem("$.DateFormat", ProjectSettingsRules.DateFormat, "not a format")]), HttpStatusCode.BadRequest, ErrorCodes.InvalidSettings },
        { new ProjectSettingsPreconditionFailedException(7), HttpStatusCode.PreconditionFailed, ErrorCodes.SettingsPreconditionFailed },
    };

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public void TryMap_ClientError_ReturnsStatusAndCodeWithoutDetails(Exception exception, HttpStatusCode expectedStatus, string expectedCode)
    {
        var mapped = ErrorResponseMapping.TryMap(exception, out var statusCode, out var response);

        mapped.ShouldBeTrue();
        statusCode.ShouldBe(expectedStatus);
        response.ShouldNotBeNull();
        response.Message.ShouldBe(exception.Message);
        response.ErrorCode.ShouldBe(expectedCode);
        response.Error.ShouldBeNull();
    }

    [Theory]
    [InlineData(typeof(Exception))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public void TryMap_OtherException_IsNotMapped(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var mapped = ErrorResponseMapping.TryMap(exception, out _, out var response);

        mapped.ShouldBeFalse();
        response.ShouldBeNull();
    }

    [Fact]
    public void ToActionResult_FailedTest_Returns412()
    {
        var result = ErrorResponseMapping.ToActionResult(new JsonPatchException("stale", JsonPatchFailureKind.TestFailed, 2));

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe((int)HttpStatusCode.PreconditionFailed);
        objectResult.Value.ShouldBeOfType<Models.ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.PatchTestFailed);
    }

    // Every problem, with its path and rule; the message names the first one and their count.
    [Fact]
    public void TryMap_InvalidProjectSettings_DetailsEveryProblem()
    {
        ProjectSettingsProblem[] problems =
        [
            new("$.Types.sbt.Color", ProjectSettingsRules.Color, "'beige' is not a color of the catalog."),
            new("$.DateFormat", ProjectSettingsRules.DateFormat, "'yy' is not a date format."),
        ];

        ErrorResponseMapping.TryMap(new ProjectSettingsInvalidException(problems), out _, out var response).ShouldBeTrue();

        response!.Details.ShouldBe(problems);
        response.Message.ShouldBe("The project settings have 2 problems; the first is $.Types.sbt.Color: 'beige' is not a color of the catalog.");
        new ProjectSettingsInvalidException([problems[1]]).Message.ShouldBe("The project settings are not valid: $.DateFormat: 'yy' is not a date format.");
    }

    [Fact]
    public void TryMap_ProjectSettingsPreconditionFailed_DetailsTheCurrentVersion()
    {
        ErrorResponseMapping.TryMap(new ProjectSettingsPreconditionFailedException(7), out _, out var response).ShouldBeTrue();

        response!.Message.ShouldBe("The project settings changed meanwhile; they are at version 7.");
        response.Details.ShouldBe(new Models.ProjectSettingsVersionDetails(7));
        FunctionTestDoubles.ToJsonBody(response.Details).ShouldBe("""{"Version":7}""");
    }

    [Fact]
    public void ToActionResult_UnmappedException_Throws()
    {
        Should.Throw<ArgumentException>(() => ErrorResponseMapping.ToActionResult(new InvalidOperationException("boom")));
    }
}
