using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk;

public partial interface IConfigurationsApiClient
{
    // GET v1/{organization}/{project}/{view}/configuration/{key} with If-None-Match: a 304 is an answer, not an error.
    Task<ConditionalResponse<Configuration>> GetConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default);

    // GET v1/{organization}/{project}/{view}/configuration/{key}/resolved with If-None-Match.
    Task<ConditionalResponse<Configuration>> GetResolvedConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default);

    // GET v1/{organization}/{project}/{view}/configuration/{key}?format={format} with If-None-Match: the Content alone as
    // flat, properties, env or yaml, as text.
    Task<ConditionalResponse<string>> GetConfigurationFormattedConditionalAsync(
        string organization, string project, string view, string key, string format, string? ifNoneMatch, CancellationToken cancellationToken = default);

    // GET v1/{organization}/{project}/{view}/configuration/{key}/resolved?format={format} with If-None-Match.
    Task<ConditionalResponse<string>> GetResolvedConfigurationFormattedConditionalAsync(
        string organization, string project, string view, string key, string format, string? ifNoneMatch, CancellationToken cancellationToken = default);
}

// Hand-written twins of GetConfigurationAsync and GetResolvedConfigurationAsync of ApiSdk.g.cs: the same route, query,
// hooks and exceptions, but a 304 returns, and the headers of the answer are kept. Keep them in step with a regeneration.
public partial class ConfigurationsApiClient
{
    private const string JsonMediaType = "application/json";

    // The media type each format answers with, sent as Accept. The server goes by the query alone, so any other value (json,
    // or one it answers with a 400) asks for anything.
    private static readonly Dictionary<string, string> FormatMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flat"] = JsonMediaType,
        ["properties"] = "text/plain",
        ["env"] = "text/plain",
        ["yaml"] = "application/yaml",
    };

    public virtual Task<ConditionalResponse<Configuration>> GetConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        return SendConditionalAsync(organization, project, view, key, false, null, JsonMediaType, ReadConfigurationAsync, ifNoneMatch, cancellationToken);
    }

    public virtual Task<ConditionalResponse<Configuration>> GetResolvedConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        return SendConditionalAsync(organization, project, view, key, true, null, JsonMediaType, ReadConfigurationAsync, ifNoneMatch, cancellationToken);
    }

    public virtual async Task<ConditionalResponse<string>> GetConfigurationFormattedConditionalAsync(
        string organization, string project, string view, string key, string format, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);

        return await SendConditionalAsync(organization, project, view, key, false, format, GetFormatMediaType(format), ReadTextAsync, ifNoneMatch, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ConditionalResponse<string>> GetResolvedConfigurationFormattedConditionalAsync(
        string organization, string project, string view, string key, string format, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);

        return await SendConditionalAsync(organization, project, view, key, true, format, GetFormatMediaType(format), ReadTextAsync, ifNoneMatch, cancellationToken).ConfigureAwait(false);
    }

    // The raw values, read before anything parses them: Cache-Control would come back reordered.
    private static Dictionary<string, IEnumerable<string>> CopyHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        return headers;
    }

    private static string GetFormatMediaType(string format)
    {
        return FormatMediaTypes.TryGetValue(format, out var mediaType) ? mediaType : "*/*";
    }

    // A formatted body is text whatever its media type, flat included; an empty body, an env without values, is "".
    private static Task<string> ReadTextAsync(
        HttpResponseMessage response, IReadOnlyDictionary<string, IEnumerable<string>> headers, CancellationToken cancellationToken)
    {
        return ReadAsStringAsync(response.Content, cancellationToken);
    }

    private async Task<Configuration> ReadConfigurationAsync(
        HttpResponseMessage response, IReadOnlyDictionary<string, IEnumerable<string>> headers, CancellationToken cancellationToken)
    {
        var objectResponse = await ReadObjectResponseAsync<Configuration>(response, headers, cancellationToken).ConfigureAwait(false);
        if (objectResponse.Object == null)
        {
            throw new ApiException("Response was null which was not expected.", (int)response.StatusCode, objectResponse.Text, headers, null);
        }

        return objectResponse.Object;
    }

    private async Task<ConditionalResponse<TResult>> SendConditionalAsync<TResult>(
        string organization,
        string project,
        string view,
        string key,
        bool resolved,
        string? format,
        string accept,
        Func<HttpResponseMessage, IReadOnlyDictionary<string, IEnumerable<string>>, CancellationToken, Task<TResult>> readResult,
        string? ifNoneMatch,
        CancellationToken cancellationToken)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);

        var client = _httpClient;

        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Get;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));

        // As the caller got it: a weak tag must not be reformatted.
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        var urlBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(_baseUrl))
        {
            urlBuilder.Append(_baseUrl);
        }

        urlBuilder.Append("v1/");
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(organization, CultureInfo.InvariantCulture)));
        urlBuilder.Append('/');
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(project, CultureInfo.InvariantCulture)));
        urlBuilder.Append('/');
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(view, CultureInfo.InvariantCulture)));
        urlBuilder.Append("/configuration/");
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(key, CultureInfo.InvariantCulture)));

        if (resolved)
        {
            urlBuilder.Append("/resolved");
        }

        // The query as the generated methods build it: without a format, the '?' is taken back.
        urlBuilder.Append('?');
        if (format != null)
        {
            urlBuilder.Append(Uri.EscapeDataString("format")).Append('=').Append(Uri.EscapeDataString(ConvertToString(format, CultureInfo.InvariantCulture))).Append('&');
        }

        urlBuilder.Length--;

        PrepareRequest(client, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(client, request, url);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers = CopyHeaders(response);

        ProcessResponse(client, response);

        var status = (int)response.StatusCode;

        if (status == 200)
        {
            var result = await readResult(response, headers, cancellationToken).ConfigureAwait(false);
            return new ConditionalResponse<TResult>(status, result, headers);
        }

        if (status == 304)
        {
            return new ConditionalResponse<TResult>(status, null, headers);
        }

        if (status == 400)
        {
            throw await ReadErrorAsync(response, headers, "The request is not well formed.", cancellationToken).ConfigureAwait(false);
        }

        if (status == 401)
        {
            var responseText = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new ApiException("Authentication issues: missing or invalid credentials, a user token without User.Impersonation, or a machine the project does not know or whose credential kind it does not enable.", status, responseText, headers, null);
        }

        if (status == 403)
        {
            throw await ReadErrorAsync(response, headers, "The caller is authenticated, but its role on the organization or project does not allow the operation (ErrorCode AccessDenied), or it is a machine restricted to an annotation that addresses another annotation or the whole project (ErrorCode AnnotationRestricted). A machine needs the scope Configuration.Read (AccessDenied).", cancellationToken).ConfigureAwait(false);
        }

        if (status == 404)
        {
            throw await ReadErrorAsync(response, headers, "The configuration doesn't exist, or the annotation is deleted (ErrorCode AnnotationDeleted).", cancellationToken).ConfigureAwait(false);
        }

        // Each read describes its own 422; the generated methods copy the description into the message.
        if (status == 422)
        {
            var message = resolved
                ? "A binding in the configuration cannot be evaluated or exceeds an evaluation limit (ErrorCode binding.evaluation or binding.limit.<kind>), or the resolved configuration cannot be written in the requested format (ErrorCode format.<rule>, with Details as on the effective read)."
                : "The configuration cannot be written in the requested format (ErrorCode format.<rule>: format.not-an-object, format.empty-name, format.colon-in-name, format.case-collision, format.bracket-in-name, format.array-gap or format.invalid-env-name). Details hold the Format, the Rule and the JSON Path; the message names the path, never a value.";
            throw await ReadErrorAsync(response, headers, message, cancellationToken).ConfigureAwait(false);
        }

        if (status == 500)
        {
            throw await ReadErrorAsync(response, headers, "Unexpected error occurred on the service.", cancellationToken).ConfigureAwait(false);
        }

        var responseData = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        throw new ApiException("The HTTP status code of the response was not expected (" + status + ").", status, responseData, headers, null);
    }

    // ApiException<ErrorResponse>, or a plain ApiException for an empty body (a 404 of a missing configuration).
    private async Task<ApiException> ReadErrorAsync(
        HttpResponseMessage response, IReadOnlyDictionary<string, IEnumerable<string>> headers, string message, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var objectResponse = await ReadObjectResponseAsync<ErrorResponse>(response, headers, cancellationToken).ConfigureAwait(false);

        return objectResponse.Object == null
            ? new ApiException("Response was null which was not expected.", status, objectResponse.Text, headers, null)
            : new ApiException<ErrorResponse>(message, status, objectResponse.Text, headers, objectResponse.Object, null);
    }
}
