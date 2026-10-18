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
    // JSON whitespace (RFC 8259); a body of nothing else is no body.
    private static ReadOnlySpan<byte> Whitespace => " \t\r\n"u8;

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    public static async Task<(T? Value, IActionResult? Error)> ReadJsonBodyAsync<T>(
        this HttpRequestData request,
        JsonSerializerOptions options,
        string description,
        string? errorCode = null)
        where T : class
    {
        var cancellationToken = request.FunctionContext.CancellationToken;

        // The bytes are deserialized as they came, not decoded to a string first, so a body that is not UTF-8
        // is a 400 too rather than JSON with replacement characters.
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);

        if (IsBlank(body.GetBuffer().AsSpan(0, (int)body.Length)))
        {
            return (null, null);
        }

        body.Position = 0;

        try
        {
            return (await JsonSerializer.DeserializeAsync<T>(body, options, cancellationToken), null);
        }
        catch (JsonException exception)
        {
            var at = string.IsNullOrEmpty(exception.Path) ? string.Empty : $" (at {exception.Path})";
            return (null, new BadRequestObjectResult(new ErrorResponse($"The request body is not {description}{at}.") { ErrorCode = errorCode }));
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> body)
    {
        if (body.StartsWith(Utf8ByteOrderMark))
        {
            body = body[Utf8ByteOrderMark.Length..];
        }

        return body.IndexOfAnyExcept(Whitespace) < 0;
    }
}
