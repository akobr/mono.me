using System.Text.Json;
using _42.Platform.Storyteller.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.V1;

// The JSON body of a handler, read by the handler rather than through [FromBody]: the worker's FromBody binding
// fails an empty body, a JSON null or a missing content type with an exception before the function runs (500).
// Here an empty body or null is no value, for the handler to name (400), and JSON that does not fit is a 400 too,
// a body without a required member included. The content type is not checked.
internal static class RequestBodyExtensions
{
    public static async Task<(T? Value, IActionResult? Error)> ReadJsonBodyAsync<T>(
        this HttpRequestData request,
        JsonSerializerOptions options,
        string description,
        string? errorCode = null)
        where T : class
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(request.FunctionContext.CancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            return (JsonSerializer.Deserialize<T>(body, options), null);
        }
        catch (JsonException exception)
        {
            var at = string.IsNullOrEmpty(exception.Path) ? string.Empty : $" (at {exception.Path})";
            return (null, new BadRequestObjectResult(new ErrorResponse($"The request body is not {description}{at}.") { ErrorCode = errorCode }));
        }
    }
}
