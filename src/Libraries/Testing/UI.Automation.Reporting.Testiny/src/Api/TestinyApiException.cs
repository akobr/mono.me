using System.Net;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// Testiny refused a request with a 4xx other than 408 and 429 (invalid data, no access, unknown entity, an update
// conflict); trying it again unchanged does not help. Error is the ApiError body when Testiny sent one.
public sealed class TestinyApiException : Exception
{
    public TestinyApiException(string message, HttpStatusCode statusCode, TestinyApiError? error = null)
        : base(message)
    {
        StatusCode = statusCode;
        Error = error;
    }

    public HttpStatusCode StatusCode { get; }

    public TestinyApiError? Error { get; }

    // The _etag of an update was stale ("Update conflict: The entity was already modified elsewhere.") or missing; the
    // OpenAPI document does not name the status, so a 409 or an error that mentions a conflict counts.
    public bool IsUpdateConflict => StatusCode == HttpStatusCode.Conflict
        || (Error?.Message?.Contains("conflict", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Error?.Code?.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ?? false);

    // The request's data was refused (400, 404, 409, 422), not the access: another item of a bulk request may still pass.
    public bool IsDataError => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
}
