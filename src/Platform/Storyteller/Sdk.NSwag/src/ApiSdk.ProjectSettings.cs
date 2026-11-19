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

public partial interface IAccessApiClient
{
    // GET v1/{organization}/{project}/settings with If-None-Match: a 304 is an answer, not an error. With the ETag of the
    // version read ("<Version>").
    Task<ConditionalResponse<ProjectSettings>> GetProjectSettingsConditionalAsync(
        string organization, string project, string? ifNoneMatch = null, CancellationToken cancellationToken = default);

    // PUT v1/{organization}/{project}/settings with If-Match, with the ETag of the stored version. The body replaces the
    // settings: a type left out goes back to its built-in appearance.
    Task<VersionedResponse<ProjectSettings>> SetProjectSettingsVersionedAsync(
        string organization, string project, ProjectSettings body, string? ifMatch = null, CancellationToken cancellationToken = default);

    // PATCH v1/{organization}/{project}/settings: a JObject as a JSON Merge Patch (application/merge-patch+json) with its
    // explicit nulls, which remove. With the ETag of the stored version.
    Task<VersionedResponse<ProjectSettings>> PatchProjectSettingsVersionedAsync(
        string organization, string project, JToken patch, string? ifMatch = null, CancellationToken cancellationToken = default);
}

// Hand-written twins of GetProjectSettingsAsync, SetProjectSettingsAsync and PatchProjectSettingsAsync of ApiSdk.g.cs that
// keep the ETag, answer a 304 of the read and send the nulls of a merge patch: the same route, hooks and exceptions. Keep
// them in step with a regeneration.
public partial class AccessApiClient
{
    private const string SettingsJsonMediaType = "application/json";
    private const string SettingsMergePatchMediaType = "application/merge-patch+json";

    private const string SettingsReadUnauthorizedMessage = "Authentication issues: missing or invalid credentials, a user token without User.Impersonation, or a machine the project does not know or whose credential kind it does not enable.";
    private const string SettingsWriteUnauthorizedMessage = "Authentication issues: missing or invalid credentials, or a token that does not act for a user (the scope User.Impersonation is missing).";
    private const string SettingsServerErrorMessage = "Unexpected error occurred on the service.";
    private const string SettingsNotFoundMessage = "The project doesn't exist.";
    private const string SettingsForbiddenWriteMessage = "Only users may call this operation: a machine is refused (ErrorCode AccessDenied), and so is a user whose role on the organization or project does not allow it (ErrorCode AccessDenied). Changing the project settings needs the Administrator role.";
    private const string SettingsConflictMessage = "The project changed concurrently twice in a row (ErrorCode Conflict); try again.";
    private const string SettingsPreconditionFailedMessage = "If-Match names another version of the settings (ErrorCode SettingsPreconditionFailed); Details.Version is the current version. Read them again and retry.";
    private const string SettingsBodyTooLargeMessage = "The request body is larger than 16 KiB (ErrorCode BodyTooLarge).";

    // The messages of the documented failures of each operation, as the generated methods throw them.
    private static readonly IReadOnlyDictionary<int, string> SettingsGetMessages = new Dictionary<int, string>
    {
        [403] = "The caller is authenticated, but its role on the project does not allow the operation (ErrorCode AccessDenied). A machine needs one of the scopes Annotation.Read or Configuration.Read (AccessDenied).",
        [404] = SettingsNotFoundMessage,
        [500] = SettingsServerErrorMessage,
    };

    private static readonly IReadOnlyDictionary<int, string> SettingsSetMessages = new Dictionary<int, string>
    {
        [400] = "The settings break a rule of the catalog (ErrorCode InvalidSettings; Details list every problem with its Path, Rule and Message, and Message names the first and their count), or a condition header is not valid (ErrorCode InvalidPrecondition).",
        [403] = SettingsForbiddenWriteMessage,
        [404] = SettingsNotFoundMessage,
        [409] = SettingsConflictMessage,
        [412] = SettingsPreconditionFailedMessage,
        [413] = SettingsBodyTooLargeMessage,
        [415] = "The body is application/merge-patch+json or application/json-patch+json (ErrorCode InvalidContentType): a PUT replaces the whole settings, so a partial body would reset every type it leaves out. Send a merge patch with PATCH. A Content-Type with several media types, or one that is no media type, is refused the same way.",
        [500] = SettingsServerErrorMessage,
    };

    private static readonly IReadOnlyDictionary<int, string> SettingsPatchMessages = new Dictionary<int, string>
    {
        [400] = "The body is not JSON or not a JSON object (ErrorCode PatchInvalid), the patched settings break a rule of the catalog (ErrorCode InvalidSettings; Details list every problem with its Path, Rule and Message, and Message names the first and their count), or a condition header is not valid (ErrorCode InvalidPrecondition).",
        [403] = SettingsForbiddenWriteMessage,
        [404] = SettingsNotFoundMessage,
        [409] = SettingsConflictMessage,
        [412] = SettingsPreconditionFailedMessage,
        [413] = SettingsBodyTooLargeMessage,
        [415] = "The body is neither application/merge-patch+json nor application/json (ErrorCode InvalidContentType); Accept-Patch names the patch format.",
        [500] = SettingsServerErrorMessage,
    };

    public virtual async Task<ConditionalResponse<ProjectSettings>> GetProjectSettingsConditionalAsync(
        string organization, string project, string? ifNoneMatch = null, CancellationToken cancellationToken = default)
    {
        var (status, settings, headers) = await SendProjectSettingsAsync(
            HttpMethod.Get, organization, project, content: null, ifMatch: null, ifNoneMatch, SettingsReadUnauthorizedMessage, SettingsGetMessages, cancellationToken).ConfigureAwait(false);

        return new ConditionalResponse<ProjectSettings>(status, settings, headers);
    }

    public virtual async Task<VersionedResponse<ProjectSettings>> SetProjectSettingsVersionedAsync(
        string organization, string project, ProjectSettings body, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var content = new StringContent(JsonConvert.SerializeObject(body, JsonSerializerSettings));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(SettingsJsonMediaType);

        var (status, settings, headers) = await SendProjectSettingsAsync(
            HttpMethod.Put, organization, project, content, ifMatch, ifNoneMatch: null, SettingsWriteUnauthorizedMessage, SettingsSetMessages, cancellationToken).ConfigureAwait(false);

        return new VersionedResponse<ProjectSettings>(status, settings!, headers);
    }

    public virtual async Task<VersionedResponse<ProjectSettings>> PatchProjectSettingsVersionedAsync(
        string organization, string project, JToken patch, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        if (patch.Type != JTokenType.Object)
        {
            throw new ArgumentException("A patch of the project settings is a JSON Merge Patch (a JObject).", nameof(patch));
        }

        // The token as it is: explicit nulls stay, and nothing reformats the values.
        var content = new StringContent(patch.ToString(Formatting.None));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(SettingsMergePatchMediaType);

        var (status, settings, headers) = await SendProjectSettingsAsync(
            HttpMethod.Patch, organization, project, content, ifMatch, ifNoneMatch: null, SettingsWriteUnauthorizedMessage, SettingsPatchMessages, cancellationToken).ConfigureAwait(false);

        return new VersionedResponse<ProjectSettings>(status, settings!, headers);
    }

    // The raw values, read before anything parses them.
    private static Dictionary<string, IEnumerable<string>> CopyProjectSettingsHeaders(HttpResponseMessage response)
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

    // The settings of a 200, or none for the 304 of a read (only GET documents it; a write throws on it as the generated
    // methods do).
    private async Task<(int Status, ProjectSettings? Settings, IReadOnlyDictionary<string, IEnumerable<string>> Headers)> SendProjectSettingsAsync(
        HttpMethod method,
        string organization,
        string project,
        HttpContent? content,
        string? ifMatch,
        string? ifNoneMatch,
        string unauthorizedMessage,
        IReadOnlyDictionary<int, string> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(project);

        var client = _httpClient;

        using var request = new HttpRequestMessage();
        request.Method = method;
        request.Content = content;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(SettingsJsonMediaType));

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
        urlBuilder.Append("/settings");

        PrepareRequest(client, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(client, request, url);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers = CopyProjectSettingsHeaders(response);

        ProcessResponse(client, response);

        var status = (int)response.StatusCode;

        if (status == 200)
        {
            var objectResponse = await ReadObjectResponseAsync<ProjectSettings>(response, headers, cancellationToken).ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new ApiException("Response was null which was not expected.", status, objectResponse.Text, headers, null);
            }

            return (status, objectResponse.Object, headers);
        }

        if (status == 304 && method == HttpMethod.Get)
        {
            return (status, null, headers);
        }

        if (status == 401)
        {
            var responseText = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new ApiException(unauthorizedMessage, status, responseText, headers, null);
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
