using System.Net;

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
        { new JsonPatchException("stale", JsonPatchFailureKind.TestFailed, 0), HttpStatusCode.PreconditionFailed, ErrorCodes.PatchTestFailed },
        { new JsonPatchException("unknown op", JsonPatchFailureKind.Invalid), HttpStatusCode.BadRequest, ErrorCodes.PatchInvalid },
        { new JsonPatchException("no path", JsonPatchFailureKind.OperationFailed, 1), HttpStatusCode.BadRequest, ErrorCodes.PatchInvalid },
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

    [Fact]
    public void ToActionResult_UnmappedException_Throws()
    {
        Should.Throw<ArgumentException>(() => ErrorResponseMapping.ToActionResult(new InvalidOperationException("boom")));
    }
}
