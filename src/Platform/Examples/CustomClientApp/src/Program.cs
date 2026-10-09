using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

// Usage: dotnet run -- [entra|authkit]
var mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "entra";

if (mode == "authkit")
{
    await AuthKitClientCredentialsAsync();
}
else
{
    await EntraClientCredentialsAsync();
}

Console.ReadLine();

// A machine registered in Entra ID (an app registration with a secret).
static async Task EntraClientCredentialsAsync()
{
    // configuration
    var tenantId = "your-tenant-id";
    var clientId = "your-client-id";
    var targetAppId = "your-storyteller-app-id";
    var secret = "your-secret";

    var app = ConfidentialClientApplicationBuilder
        .Create(clientId)
        .WithClientSecret(secret)
        .Build();

    var authResult = await app.AcquireTokenForClient(scopes: new[] { $"api://{targetAppId}/.default" })
        .WithTenantId(tenantId)
        .ExecuteAsync();

    Console.WriteLine($"AccessToken: {authResult.AccessToken}");
}

// A machine created by `sform machine create` in a project with the ClientCredentials policy:
// a WorkOS AuthKit M2M application. A plain OAuth 2.0 client_credentials POST, no SDK needed.
static async Task AuthKitClientCredentialsAsync()
{
    // configuration, as printed by `sform machine create`
    var tokenUrl = "https://your-subdomain.authkit.app/oauth2/token";
    var clientId = "client_your-machine-client-id";
    var clientSecret = "your-client-secret";

    // optional: a Storyteller call made with the token
    var storytellerBaseUrl = "https://your-storyteller.azurewebsites.net/api";
    var organization = "your-organization";
    var project = "your-project";

    using var http = new HttpClient();
    using var tokenResponse = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
    }));

    tokenResponse.EnsureSuccessStatusCode();
    using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
    var accessToken = token.RootElement.GetProperty("access_token").GetString();
    Console.WriteLine($"AccessToken: {accessToken}");

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{storytellerBaseUrl}/v1/{organization}/{project}/default/annotations");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    using var response = await http.SendAsync(request);
    Console.WriteLine($"GET annotations: {(int)response.StatusCode} {response.StatusCode}");
}
