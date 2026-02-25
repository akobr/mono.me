using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

public class KeycloakMachineAccessService(IHttpClientFactory httpClientFactory) : IMachineAccessService
{
    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var realm = GetRealm();
        var serverUrl = GetServerUrl();
        var clientId = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}";

        using var client = httpClientFactory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Create the client
        var clientDescription = new JsonObject
        {
            ["clientId"] = clientId,
            ["name"] = clientId,
            ["description"] = $"organization={model.Organization}|project={model.Project}|scope={model.Scope:G}{(model.AnnotationKey != null ? $"|annotation={model.AnnotationKey}" : string.Empty)}",
            ["enabled"] = true,
            ["serviceAccountsEnabled"] = true,
            ["publicClient"] = false,
            ["protocol"] = "openid-connect",
            ["attributes"] = new JsonObject
            {
                ["42.organization"] = model.Organization,
                ["42.project"] = model.Project,
                ["42.scope"] = model.Scope.ToString("G"),
            },
        };

        if (model.AnnotationKey != null)
        {
            clientDescription["attributes"]!["42.annotation"] = model.AnnotationKey;
        }

        var response = await client.PostAsJsonAsync($"{serverUrl}/admin/realms/{realm}/clients", clientDescription);
        response.EnsureSuccessStatusCode();

        // 2. Get the internal ID of the created client (Keycloak returns it in Location header or we can fetch it)
        var location = response.Headers.Location;
        var internalId = location?.Segments.Last() ?? throw new InvalidOperationException("Failed to get internal ID of the created client.");

        // 3. Get the client secret
        var secretResponse = await client.GetAsync($"{serverUrl}/admin/realms/{realm}/clients/{internalId}/client-secret");
        secretResponse.EnsureSuccessStatusCode();
        var secretData = await secretResponse.Content.ReadFromJsonAsync<JsonObject>();
        var clientSecret = secretData?["value"]?.ToString() ?? throw new InvalidOperationException("Failed to get client secret.");

        // TODO: [P2] Assign roles/scopes to the service account if needed.
        // For now, we just rely on the fact that it's a service account and we store the scope in attributes/description.
        return new MachineAccess
        {
            Id = clientId,
            ObjectId = internalId,
            AccessKey = clientSecret,
            AnnotationKey = model.AnnotationKey,
            Scope = model.Scope <= MachineAccessScope.ConfigurationRead
                ? MachineAccessScope.DefaultRead
                : MachineAccessScope.DefaultReadWrite,
        };
    }

    public async Task<string?> ResetMachineAccessAsync(string objectId)
    {
        var realm = GetRealm();
        var serverUrl = GetServerUrl();

        using var client = httpClientFactory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"{serverUrl}/admin/realms/{realm}/clients/{objectId}/client-secret", null);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var secretData = await response.Content.ReadFromJsonAsync<JsonObject>();
        return secretData?["value"]?.ToString();
    }

    public async Task<bool> DeleteMachineAccessAsync(string objectId)
    {
        var realm = GetRealm();
        var serverUrl = GetServerUrl();

        using var client = httpClientFactory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"{serverUrl}/admin/realms/{realm}/clients/{objectId}");
        return response.IsSuccessStatusCode;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var serverUrl = GetServerUrl();
        var adminRealm = Environment.GetEnvironmentVariable("Keycloak:AdminRealm") ?? "master";
        var adminClientId = Environment.GetEnvironmentVariable("Keycloak:AdminClientId") ?? "admin-cli";
        var adminClientSecret = Environment.GetEnvironmentVariable("Keycloak:AdminClientSecret");
        var username = Environment.GetEnvironmentVariable("Keycloak:AdminUsername");
        var password = Environment.GetEnvironmentVariable("Keycloak:AdminPassword");

        var contentList = new List<KeyValuePair<string, string>>
        {
            new("client_id", adminClientId),
        };

        if (!string.IsNullOrEmpty(adminClientSecret))
        {
            contentList.Add(new KeyValuePair<string, string>("client_secret", adminClientSecret));
            contentList.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
        }
        else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            contentList.Add(new KeyValuePair<string, string>("grant_type", "password"));
            contentList.Add(new KeyValuePair<string, string>("username", username));
            contentList.Add(new KeyValuePair<string, string>("password", password));
        }
        else
        {
            throw new InvalidOperationException("Missing Keycloak admin credentials.");
        }

        var response = await client.PostAsync($"{serverUrl}/realms/{adminRealm}/protocol/openid-connect/token", new FormUrlEncodedContent(contentList));
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<JsonObject>();
        return data?["access_token"]?.ToString() ?? throw new InvalidOperationException("Failed to get admin token.");
    }

    private static string GetRealm() => Environment.GetEnvironmentVariable("Keycloak:Realm") ?? "storyteller";

    private static string GetServerUrl() => Environment.GetEnvironmentVariable("Keycloak:ServerUrl") ?? "http://localhost:8080";
}
