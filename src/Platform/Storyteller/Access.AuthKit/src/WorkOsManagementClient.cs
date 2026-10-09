using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Thin client for the WorkOS management API, authenticated with the sk_… key.
// The key is sent only in the Authorization header and is never logged.
// Retries are configured on the HttpClient (AuthKitEntryPoint), never for a create.
public class WorkOsManagementClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public WorkOsManagementClient(HttpClient httpClient, IOptions<UserAuthenticationOptions> options)
    {
        var authKit = options.Value.AuthKit;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri($"{authKit.ApiBaseUrl.TrimEnd('/')}/");
        _apiKey = string.IsNullOrWhiteSpace(authKit.ApiKey) ? null : authKit.ApiKey;
    }

    public bool IsConfigured => _apiKey is not null;

    // Null when WorkOS has no user with this id.
    public virtual async Task<WorkOsUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"user_management/users/{Uri.EscapeDataString(userId)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOsUser>(cancellationToken);
    }

    public virtual async Task<WorkOsConnectApplication> CreateM2MApplicationAsync(
        WorkOsM2MApplicationCreate application,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "connect/applications");
        request.Content = JsonContent.Create(application);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOsConnectApplication>(cancellationToken)
            ?? throw new WorkOsApiException(response.StatusCode, "WorkOS returned an empty application.");
    }

    // False when the application does not exist (already deleted).
    public virtual async Task<bool> DeleteApplicationAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"connect/applications/{Uri.EscapeDataString(applicationId)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    // The plaintext secret is only in this response.
    public virtual async Task<WorkOsClientSecret> CreateClientSecretAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"connect/applications/{Uri.EscapeDataString(applicationId)}/client_secrets");
        request.Content = JsonContent.Create(new { });
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var secret = await response.Content.ReadFromJsonAsync<WorkOsClientSecret>(cancellationToken);

        return string.IsNullOrEmpty(secret?.Secret)
            ? throw new WorkOsApiException(response.StatusCode, "WorkOS returned a client secret without its value.")
            : secret;
    }

    public virtual async Task<IReadOnlyList<WorkOsClientSecret>> ListClientSecretsAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"connect/applications/{Uri.EscapeDataString(applicationId)}/client_secrets");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        // The reference shows a bare array; accept a { "data": [...] } list as well.
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("data", out var data) ? data : default;

        return items.ValueKind == JsonValueKind.Array
            ? items.Deserialize<List<WorkOsClientSecret>>() ?? []
            : [];
    }

    // False when the secret does not exist (already deleted).
    public virtual async Task<bool> DeleteClientSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"connect/client_secrets/{Uri.EscapeDataString(secretId)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    // WorkOS sends the invitation email. Never retried except on 429, so one call sends at most one email.
    public virtual async Task<WorkOsInvitation> SendInvitationAsync(WorkOsInvitationCreate invitation, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "user_management/invitations");
        request.Content = JsonContent.Create(invitation);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<WorkOsInvitation>(cancellationToken);

        return string.IsNullOrEmpty(created?.Id)
            ? throw new WorkOsApiException(response.StatusCode, "WorkOS returned an invitation without its id.")
            : created;
    }

    // False when the invitation does not exist.
    public virtual async Task<bool> RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"user_management/invitations/{Uri.EscapeDataString(invitationId)}/revoke");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            detail = ReadString(root, "message") ?? ReadString(root, "error_description") ?? ReadString(root, "code") ?? ReadString(root, "error");
        }
        catch (JsonException)
        {
            // Not a JSON body; the status code is enough.
        }

        throw new WorkOsApiException(
            response.StatusCode,
            $"WorkOS API returned {(int)response.StatusCode} ({response.StatusCode}){(detail is null ? "." : $": {detail}")}");
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (_apiKey is null)
        {
            throw new InvalidOperationException("Auth:AuthKit:ApiKey is required to call the WorkOS management API.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }
}
