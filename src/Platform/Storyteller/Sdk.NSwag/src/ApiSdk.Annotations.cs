using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk;

public partial interface IAnnotationsApiClient
{
    // GET v1/{organization}/{project}/{view}/annotations/{key}, with the ETag of the version read.
    Task<VersionedResponse<Annotation>> GetAnnotationVersionedAsync(
        string organization, string project, string view, string key, bool? includeInactive = null, CancellationToken cancellationToken = default);

    // POST v1/{organization}/{project}/{view}/annotations/{key} with If-Match or If-None-Match, with the ETag of the
    // written version. The body replaces the editable content.
    Task<VersionedResponse<Annotation>> SetAnnotationVersionedAsync(
        string organization, string project, string view, string key, Annotation body, string? ifMatch = null, string? ifNoneMatch = null, CancellationToken cancellationToken = default);

    // PATCH v1/{organization}/{project}/{view}/annotations/{key}: a JArray goes as a JSON Patch (application/json-patch+json,
    // see AnnotationJsonPatch), a JObject as a JSON Merge Patch (application/merge-patch+json) with its explicit nulls.
    // With the ETag of the written version.
    Task<VersionedResponse<Annotation>> PatchAnnotationVersionedAsync(
        string organization, string project, string view, string key, JToken patch, string? ifMatch = null, CancellationToken cancellationToken = default);
}

// Hand-written twins of GetAnnotationAsync, SetAnnotationAsync and PatchAnnotationAsync of ApiSdk.g.cs that keep the
// ETag: the same route, query, hooks and exceptions. Keep them in step with a regeneration.
public partial class AnnotationsApiClient
{
    private const string AnnotationJsonMediaType = "application/json";
    private const string MergePatchMediaType = "application/merge-patch+json";
    private const string JsonPatchMediaType = "application/json-patch+json";

    private const string UnauthorizedMessage = "Authentication issues: missing or invalid credentials, a user token without User.Impersonation, or a machine the project does not know or whose credential kind it does not enable.";
    private const string ServerErrorMessage = "Unexpected error occurred on the service.";
    private const string ConflictMessage = "The annotations changed concurrently three times in a row (Conflict); try again.";
    private const string ForbiddenWriteMessage = "The caller is authenticated, but its role on the organization or project does not allow the operation (ErrorCode AccessDenied), or it is a machine restricted to an annotation that addresses another annotation or the whole project (ErrorCode AnnotationRestricted). A machine needs the scope Annotation.Write (AccessDenied).";

    // The messages of the documented failures of each operation, as the generated methods throw them.
    private static readonly IReadOnlyDictionary<int, string> GetMessages = new Dictionary<int, string>
    {
        [400] = "The request is not well formed.",
        [403] = "The caller is authenticated, but its role on the organization or project does not allow the operation (ErrorCode AccessDenied), or it is a machine restricted to an annotation that addresses another annotation or the whole project (ErrorCode AnnotationRestricted). A machine needs the scope Annotation.Read (AccessDenied).",
        [404] = "The requested annotation doesn't exist or is deleted, or it is not active and includeInactive is not set (ErrorCode AnnotationInactive).",
        [500] = ServerErrorMessage,
    };

    private static readonly IReadOnlyDictionary<int, string> SetMessages = new Dictionary<int, string>
    {
        [400] = "The request is not well formed.",
        [403] = ForbiddenWriteMessage,
        [409] = ConflictMessage,
        [412] = "A condition of the write does not hold (ErrorCode AnnotationPreconditionFailed): If-Match names another version, or the annotation does not exist or is deleted; If-None-Match: * and the annotation exists. Read it again and retry.",
        [500] = ServerErrorMessage,
    };

    private static readonly IReadOnlyDictionary<int, string> PatchMessages = new Dictionary<int, string>
    {
        [400] = "The body is not JSON or not a patch (ErrorCode PatchInvalid), a JSON Patch operation failed or the patched annotation is not valid (ErrorCode PatchInvalid or InvalidAnnotation), or a condition header is not valid (ErrorCode InvalidPrecondition).",
        [403] = ForbiddenWriteMessage,
        [404] = "The annotation does not exist (ErrorCode AnnotationNotFound) or is deleted (ErrorCode AnnotationDeleted); a patch never creates it.",
        [409] = ConflictMessage,
        [412] = "If-Match names another version of the annotation (ErrorCode AnnotationPreconditionFailed), or a JSON Patch test operation did not match (ErrorCode PatchTestFailed). Read it again and retry.",
        [415] = "The body is neither application/merge-patch+json, application/json-patch+json nor application/json (ErrorCode InvalidContentType); Accept-Patch lists the patch formats.",
        [500] = ServerErrorMessage,
    };

    public virtual Task<VersionedResponse<Annotation>> GetAnnotationVersionedAsync(
        string organization, string project, string view, string key, bool? includeInactive = null, CancellationToken cancellationToken = default)
    {
        return SendVersionedAsync(HttpMethod.Get, organization, project, view, key, includeInactive, content: null, ifMatch: null, ifNoneMatch: null, GetMessages, cancellationToken);
    }

    public virtual Task<VersionedResponse<Annotation>> SetAnnotationVersionedAsync(
        string organization, string project, string view, string key, Annotation body, string? ifMatch = null, string? ifNoneMatch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var content = new StringContent(JsonConvert.SerializeObject(body, JsonSerializerSettings));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(AnnotationJsonMediaType);
        return SendVersionedAsync(HttpMethod.Post, organization, project, view, key, includeInactive: null, content, ifMatch, ifNoneMatch, SetMessages, cancellationToken);
    }

    public virtual Task<VersionedResponse<Annotation>> PatchAnnotationVersionedAsync(
        string organization, string project, string view, string key, JToken patch, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var mediaType = patch.Type switch
        {
            JTokenType.Array => JsonPatchMediaType,
            JTokenType.Object => MergePatchMediaType,
            _ => throw new ArgumentException("A patch is a JSON Patch (a JArray) or a JSON Merge Patch (a JObject).", nameof(patch)),
        };

        // The token as it is: explicit nulls stay, and nothing reformats the values.
        var content = new StringContent(patch.ToString(Formatting.None));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return SendVersionedAsync(HttpMethod.Patch, organization, project, view, key, includeInactive: null, content, ifMatch, ifNoneMatch: null, PatchMessages, cancellationToken);
    }

    // The raw values, read before anything parses them.
    private static Dictionary<string, IEnumerable<string>> CopyAnnotationHeaders(HttpResponseMessage response)
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

    private async Task<VersionedResponse<Annotation>> SendVersionedAsync(
        HttpMethod method,
        string organization,
        string project,
        string view,
        string key,
        bool? includeInactive,
        HttpContent? content,
        string? ifMatch,
        string? ifNoneMatch,
        IReadOnlyDictionary<int, string> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);

        var client = _httpClient;

        using var request = new HttpRequestMessage();
        request.Method = method;
        request.Content = content;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(AnnotationJsonMediaType));

        // As the caller got them: a tag must not be reformatted.
        if (!string.IsNullOrEmpty(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

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
        urlBuilder.Append("/annotations/");
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(key, CultureInfo.InvariantCulture)));

        // The query as the generated read builds it: without a parameter, the '?' is taken back.
        if (method == HttpMethod.Get)
        {
            urlBuilder.Append('?');
            if (includeInactive != null)
            {
                urlBuilder.Append(Uri.EscapeDataString("includeInactive")).Append('=').Append(Uri.EscapeDataString(ConvertToString(includeInactive, CultureInfo.InvariantCulture))).Append('&');
            }

            urlBuilder.Length--;
        }

        PrepareRequest(client, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(client, request, url);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers = CopyAnnotationHeaders(response);

        ProcessResponse(client, response);

        var status = (int)response.StatusCode;

        if (status == 200)
        {
            var objectResponse = await ReadObjectResponseAsync<Annotation>(response, headers, cancellationToken).ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new ApiException("Response was null which was not expected.", status, objectResponse.Text, headers, null);
            }

            return new VersionedResponse<Annotation>(status, objectResponse.Object, headers);
        }

        if (status == 401)
        {
            var responseText = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new ApiException(UnauthorizedMessage, status, responseText, headers, null);
        }

        if (messages.TryGetValue(status, out var message))
        {
            var errorResponse = await ReadObjectResponseAsync<ErrorResponse>(response, headers, cancellationToken).ConfigureAwait(false);
            throw errorResponse.Object == null
                ? new ApiException("Response was null which was not expected.", status, errorResponse.Text, headers, null)
                : new ApiException<ErrorResponse>(message, status, errorResponse.Text, headers, errorResponse.Object, null);
        }

        var responseData = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        throw new ApiException("The HTTP status code of the response was not expected (" + status + ").", status, responseData, headers, null);
    }
}
