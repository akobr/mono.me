using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Machine access as Keycloak confidential clients with a service account. MachineAccess.Id is the
// clientId (the token azp), ObjectId the client's internal ID. Two protocol mappers make the tokens
// self-describing: the API audience and the machine's Storyteller scopes (storyteller_scope).
public class KeycloakMachineAccessService(IHttpClientFactory httpClientFactory, IOptions<KeycloakOptions> options)
    : IIdentityProviderMachineAccessService
{
    public const string ScopeClaimType = "storyteller_scope";

    private readonly KeycloakOptions _options = options.Value;

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var clientId = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}";

        using var client = await CreateAdminClientAsync();

        // 1. Create the client
        var response = await client.PostAsJsonAsync(AdminUrl("clients"), BuildClient(clientId, model));
        response.EnsureSuccessStatusCode();

        // 2. The internal ID of the created client is the last segment of the Location header
        var internalId = response.Headers.Location?.Segments.Last()
            ?? throw new InvalidOperationException("Failed to get internal ID of the created client.");

        try
        {
            // 3. Get the client secret
            var secretResponse = await client.GetAsync(AdminUrl($"clients/{internalId}/client-secret"));
            secretResponse.EnsureSuccessStatusCode();
            var secretData = await secretResponse.Content.ReadFromJsonAsync<JsonObject>();
            var clientSecret = secretData?["value"]?.ToString() ?? throw new InvalidOperationException("Failed to get client secret.");

            return new MachineAccess
            {
                Id = clientId,
                ObjectId = internalId,
                AccessKey = clientSecret,
                AnnotationKey = model.AnnotationKey,
                Scope = model.Scope,
                CredentialKind = MachineCredentialKind.ClientCredentials,
                TokenEndpoint = _options.GetTokenEndpoint(),
            };
        }
        catch
        {
            // Compensation: a client without a secret the caller ever saw is useless.
            await client.DeleteAsync(AdminUrl($"clients/{internalId}"));
            throw;
        }
    }

    // Regenerating the secret invalidates the previous one.
    public async Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(AdminUrl($"clients/{objectId}/client-secret"), null);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var secretData = await response.Content.ReadFromJsonAsync<JsonObject>();
        return secretData?["value"]?.ToString();
    }

    public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return ResetMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    // False when the client no longer exists; other failures throw, so the machine is not
    // forgotten in Storyteller while its client still works.
    public async Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.DeleteAsync(AdminUrl($"clients/{objectId}"));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    internal JsonObject BuildClient(string clientId, MachineAccessCreate model)
    {
        var attributes = new JsonObject
        {
            ["42.organization"] = model.Organization,
            ["42.project"] = model.Project,
            ["42.scope"] = model.Scope.ToString("G"),
        };

        if (model.AnnotationKey != null)
        {
            attributes["42.annotation"] = model.AnnotationKey;
        }

        return new JsonObject
        {
            ["clientId"] = clientId,
            ["name"] = clientId,
            ["description"] = $"organization={model.Organization}|project={model.Project}|scope={model.Scope:G}{(model.AnnotationKey != null ? $"|annotation={model.AnnotationKey}" : string.Empty)}",
            ["enabled"] = true,
            ["serviceAccountsEnabled"] = true,
            ["standardFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = false,
            ["publicClient"] = false,
            ["protocol"] = "openid-connect",
            ["attributes"] = attributes,
            ["protocolMappers"] = new JsonArray
            {
                AccessTokenMapper("storyteller-audience", "oidc-audience-mapper", new JsonObject
                {
                    ["included.custom.audience"] = _options.Audience,
                }),
                AccessTokenMapper("storyteller-scope", "oidc-hardcoded-claim-mapper", new JsonObject
                {
                    ["claim.name"] = ScopeClaimType,
                    ["claim.value"] = string.Join(' ', MachineAccessScopes.Get(model.Scope)),
                    ["jsonType.label"] = "String",
                    ["userinfo.token.claim"] = "false",
                }),
            },
        };
    }

    private static JsonObject AccessTokenMapper(string name, string mapper, JsonObject config)
    {
        config["access.token.claim"] = "true";
        config["id.token.claim"] = "false";

        return new JsonObject
        {
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = mapper,
            ["config"] = config,
        };
    }

    private string AdminUrl(string path)
    {
        return $"{_options.ServerUrl.TrimEnd('/')}/admin/realms/{_options.Realm}/{path}";
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = httpClientFactory.CreateClient(nameof(KeycloakMachineAccessService));

        try
        {
            var token = await GetAdminTokenAsync(client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var contentList = new List<KeyValuePair<string, string>>
        {
            new("client_id", _options.AdminClientId),
        };

        if (!string.IsNullOrEmpty(_options.AdminClientSecret))
        {
            contentList.Add(new KeyValuePair<string, string>("client_secret", _options.AdminClientSecret));
            contentList.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
        }
        else if (!string.IsNullOrEmpty(_options.AdminUsername) && !string.IsNullOrEmpty(_options.AdminPassword))
        {
            contentList.Add(new KeyValuePair<string, string>("grant_type", "password"));
            contentList.Add(new KeyValuePair<string, string>("username", _options.AdminUsername));
            contentList.Add(new KeyValuePair<string, string>("password", _options.AdminPassword));
        }
        else
        {
            throw new InvalidOperationException("Missing Keycloak admin credentials.");
        }

        var response = await client.PostAsync(
            $"{_options.ServerUrl.TrimEnd('/')}/realms/{_options.AdminRealm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(contentList));
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<JsonObject>();
        return data?["access_token"]?.ToString() ?? throw new InvalidOperationException("Failed to get admin token.");
    }
}
