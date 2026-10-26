using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.V1;

// The transport and form rules of the token endpoints (RFC 6749 sections 3.2 and 5), shared by the machine exchange of
// FederationHttp and the user exchange of TokenExchangeHttp, and the OAuth answers they write.
internal static class OAuthTokenForm
{
    public const string RetryAfterSeconds = "5";

    private static readonly JsonSerializerOptions TokenJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // RFC 8693 section 2.1 lets audience and resource repeat; any other parameter appears once (RFC 6749 section 3.2).
    private static readonly HashSet<string> RepeatableParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "audience",
        "resource",
    };

    // The parameters of the form with their non-empty values, or the description of the invalid_request: an
    // Authorization header, another content type, a body over maxBytes, or a parameter given twice. A repeated parameter
    // is named only when it is one of knownParameters, so the answer never echoes what the client made up.
    public static async Task<(Dictionary<string, string[]>? Parameters, string? Error)> ReadAsync(
        HttpRequestData request,
        int maxBytes,
        IReadOnlySet<string> knownParameters,
        CancellationToken cancellationToken)
    {
        // A bearer header would already have been validated as an API credential; the form carries the credentials.
        if (request.Headers.Contains("Authorization"))
        {
            return (null, "The token request must not carry an Authorization header.");
        }

        if (!IsFormContent(request.Headers))
        {
            return (null, $"The content type must be {Definitions.ContentTypes.FormUrlEncoded}.");
        }

        var body = await ReadBodyAsync(request.Body, maxBytes, cancellationToken);

        if (body is null)
        {
            return (null, $"The request body exceeds {maxBytes} bytes.");
        }

        // An empty value counts as absent (RFC 6749 section 3.2).
        var parameters = QueryHelpers.ParseQuery(body)
            .Select(parameter => (Name: parameter.Key, Values: parameter.Value.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).ToArray()))
            .Where(parameter => parameter.Values.Length > 0)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Values, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in parameters)
        {
            if (values.Length > 1 && !RepeatableParameters.Contains(name))
            {
                return (null, knownParameters.Contains(name)
                    ? $"The parameter {name.ToLowerInvariant()} is given more than once."
                    : "A parameter is given more than once.");
            }
        }

        return (parameters, null);
    }

    public static string? GetSingle(Dictionary<string, string[]> parameters, string name)
    {
        return parameters.TryGetValue(name, out var values) ? values[0] : null;
    }

    public static string[] GetAll(Dictionary<string, string[]> parameters, string name)
    {
        return parameters.TryGetValue(name, out var values) ? values : [];
    }

    // {PublicBaseUrl, or the request's scheme and host with /api}: the base of the URLs the API publishes about itself.
    // Never from X-Forwarded-Host, so a client cannot make the API name another host.
    public static string GetApiBaseUrl(HttpRequestData request, FederationOptions options)
    {
        return string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? $"{request.Url.Scheme}://{request.Url.Authority}{FederationHttp.RoutePrefix}"
            : options.PublicBaseUrl.TrimEnd('/');
    }

    // OAuth responses go out as they are, not through ErrorResponse, and are never cached (RFC 6749 section 5.1).
    public static async Task<HttpResponseData> RespondAsync(HttpRequestData request, HttpStatusCode statusCode, object body)
    {
        var response = CreateJsonResponse(request, statusCode);
        response.Headers.Add("Cache-Control", "no-store");
        response.Headers.Add("Pragma", "no-cache");

        if (statusCode == HttpStatusCode.ServiceUnavailable)
        {
            response.Headers.Add("Retry-After", RetryAfterSeconds);
        }

        await WriteJsonAsync(response, body);
        return response;
    }

    public static HttpResponseData CreateJsonResponse(HttpRequestData request, HttpStatusCode statusCode)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        return response;
    }

    // The body named by its System.Text.Json attributes, without null members.
    public static async Task WriteJsonAsync(HttpResponseData response, object body)
    {
        await response.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), TokenJsonOptions));
    }

    // One Content-Type of application/x-www-form-urlencoded; parameters such as charset are allowed.
    private static bool IsFormContent(HttpHeadersCollection headers)
    {
        if (!headers.TryGetValues("Content-Type", out var values))
        {
            return false;
        }

        var contentTypes = values.ToList();

        return contentTypes.Count == 1
            && MediaTypeHeaderValue.TryParse(contentTypes[0], out var mediaType)
            && string.Equals(mediaType.MediaType, Definitions.ContentTypes.FormUrlEncoded, StringComparison.OrdinalIgnoreCase);
    }

    // Null when the body is longer than maxBytes; nothing beyond one more byte is read.
    private static async Task<string?> ReadBodyAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[maxBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length > maxBytes ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }
}
