using Microsoft.Identity.Client;

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

Console.ReadLine();
