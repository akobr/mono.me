using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Thin client for the WorkOS management API, authenticated with the sk_… key.
// The key is sent only in the Authorization header and is never logged.
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

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkOsUser>(cancellationToken);
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
