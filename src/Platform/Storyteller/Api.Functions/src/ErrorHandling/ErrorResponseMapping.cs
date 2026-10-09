using System.Diagnostics.CodeAnalysis;
using System.Net;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Configuring;
using Microsoft.AspNetCore.Mvc;

namespace _42.Platform.Storyteller.Api.ErrorHandling;

// Client errors raised as exceptions. The response carries the message and the error code, never exception details.
public static class ErrorResponseMapping
{
    public static bool TryMap(Exception exception, out HttpStatusCode statusCode, [NotNullWhen(true)] out ErrorResponse? response)
    {
        (statusCode, var errorCode) = exception switch
        {
            AccessDeniedException e => (HttpStatusCode.Forbidden, e.ErrorCode),
            NotFoundException e => (HttpStatusCode.NotFound, e.ErrorCode),
            ConflictException e => (HttpStatusCode.Conflict, e.ErrorCode),
            JsonPatchException { Kind: JsonPatchFailureKind.TestFailed } e => (HttpStatusCode.PreconditionFailed, e.ErrorCode),
            JsonPatchException e => (HttpStatusCode.BadRequest, e.ErrorCode),
            _ => (HttpStatusCode.InternalServerError, null),
        };

        if (errorCode is null)
        {
            response = null;
            return false;
        }

        response = new ErrorResponse(exception.Message)
        {
            ErrorCode = errorCode,
            Hint = exception.TryGetErrorHint(),
        };
        return true;
    }

    public static IActionResult ToActionResult(Exception exception)
    {
        if (!TryMap(exception, out var statusCode, out var response))
        {
            throw new ArgumentException($"The exception {exception.GetType().Name} has no client error mapping.", nameof(exception));
        }

        return new ObjectResult(response) { StatusCode = (int)statusCode };
    }
}
