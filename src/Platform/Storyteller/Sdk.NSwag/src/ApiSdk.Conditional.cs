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
}

// Hand-written twins of GetConfigurationAsync and GetResolvedConfigurationAsync of ApiSdk.g.cs: the same route, hooks
// and exceptions, but a 304 returns, and the headers of the answer are kept. Keep them in step with a regeneration.
public partial class ConfigurationsApiClient
{
    public virtual Task<ConditionalResponse<Configuration>> GetConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        return SendConditionalAsync(organization, project, view, key, false, ifNoneMatch, cancellationToken);
    }

    public virtual Task<ConditionalResponse<Configuration>> GetResolvedConfigurationConditionalAsync(
        string organization, string project, string view, string key, string? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        return SendConditionalAsync(organization, project, view, key, true, ifNoneMatch, cancellationToken);
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

    private async Task<ConditionalResponse<Configuration>> SendConditionalAsync(
        string organization, string project, string view, string key, bool resolved, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);

        var client = _httpClient;

        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Get;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

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
            var objectResponse = await ReadObjectResponseAsync<Configuration>(response, headers, cancellationToken).ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new ApiException("Response was null which was not expected.", status, objectResponse.Text, headers, null);
            }

            return new ConditionalResponse<Configuration>(status, objectResponse.Object, headers);
        }

        if (status == 304)
        {
            return new ConditionalResponse<Configuration>(status, null, headers);
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

        // Only the resolved read declares 422; for the effective read it is unexpected, as in the generated method.
        if (status == 422 && resolved)
        {
            throw await ReadErrorAsync(response, headers, "A binding in the configuration cannot be evaluated or exceeds an evaluation limit. ErrorCode is binding.evaluation or binding.limit.<kind>.", cancellationToken).ConfigureAwait(false);
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
