using System.Net;
using System.Text.Json;
using _42.Platform.Storyteller.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.V1;

// The JSON body of a handler, read by the handler rather than through [FromBody]: the worker's FromBody binding
// fails an empty body, a JSON null or a missing content type with an exception before the function runs (500).
// Here an empty body or null is no value, for the handler to name (400), and JSON that does not fit is a 400 too,
// a body without a required member included. The content type is not checked. A body larger than the handler reads
// (RequestBodyLimits) is a 413, before any of it is parsed.
internal static class RequestBodyExtensions
{
    private const int CopyBufferBytes = 81920;

    // JSON whitespace (RFC 8259); a body of nothing else is no body.
    private static ReadOnlySpan<byte> Whitespace => " \t\r\n"u8;

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    public static async Task<(T? Value, IActionResult? Error)> ReadJsonBodyAsync<T>(
        this HttpRequestData request,
        JsonSerializerOptions options,
        string description,
        int maxBytes,
        string? errorCode = null)
        where T : class
    {
        var cancellationToken = request.FunctionContext.CancellationToken;

        // The bytes are deserialized as they came, not decoded to a string first, so a body that is not UTF-8
        // is a 400 too rather than JSON with replacement characters.
        using var body = new MemoryStream();

        if (!await request.TryCopyBodyAsync(body, maxBytes, cancellationToken))
        {
            return (null, BodyTooLarge(maxBytes));
        }

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

    // False when the declared Content-Length or the body itself is larger than maxBytes: nothing is read past the
    // read that crosses the limit, and that read is not copied.
    public static async Task<bool> TryCopyBodyAsync(this HttpRequestData request, Stream target, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues("Content-Length", out var lengths)
            && long.TryParse(lengths.FirstOrDefault(), out var length)
            && length > maxBytes)
        {
            return false;
        }

        var buffer = new byte[Math.Min(CopyBufferBytes, maxBytes + 1L)];
        var copied = 0L;
        int read;

        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied += read;

            if (copied > maxBytes)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return true;
    }

    public static ObjectResult BodyTooLarge(int maxBytes)
    {
        return new ObjectResult(new ErrorResponse($"The request body is larger than {maxBytes} bytes.") { ErrorCode = ErrorCodes.BodyTooLarge })
        {
            StatusCode = (int)HttpStatusCode.RequestEntityTooLarge,
        };
    }

    // The bytes after an optional UTF-8 byte order mark, which a JSON reader of bytes does not skip itself.
    public static ReadOnlyMemory<byte> WithoutByteOrderMark(this MemoryStream body)
    {
        var bytes = body.GetBuffer().AsMemory(0, (int)body.Length);
        return bytes.Span.StartsWith(Utf8ByteOrderMark) ? bytes[Utf8ByteOrderMark.Length..] : bytes;
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
