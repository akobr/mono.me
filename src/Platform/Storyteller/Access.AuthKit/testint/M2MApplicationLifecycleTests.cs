using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit.Abstractions;

namespace _42.Platform.Storyteller.Access.AuthKit.IntegrationTests;

// Against a real WorkOS staging environment. Creates one M2M application and deletes it again.
// The permission in WORKOS_TEST_PERMISSION must exist in the environment.
public class M2MApplicationLifecycleTests
{
    private readonly ITestOutputHelper _output;

    public M2MApplicationLifecycleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task M2MApplication_IssuesATokenStorytellerAccepts_ThenResetsAndDeletes()
    {
        var settings = WorkOsTestSettings.TryRead();

        if (settings is null)
        {
            _output.WriteLine($"Skipped: set {WorkOsTestSettings.Variables} to run against WorkOS.");
            return;
        }

        var options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = settings.ClientId,
                ApiKey = settings.ApiKey,
                AuthKitDomain = settings.AuthKitDomain,
                MachineOrganizationId = settings.OrganizationId,
                PermissionMap = new Dictionary<string, string> { [settings.Permission] = "Annotation.Read" },
            },
        });
        using var http = new HttpClient();
        var service = new AuthKitMachineAccessService(
            new WorkOsManagementClient(new HttpClient(), options),
            options,
            NullLogger<AuthKitMachineAccessService>.Instance);

        var access = await service.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "integration",
            Project = "authkit",
            Scope = MachineAccessScope.AnnotationRead,
        });
        _output.WriteLine($"Created {access.ObjectId} ({access.Id}).");

        try
        {
            var token = await RequestTokenAsync(http, settings.AuthKitDomain, access.Id, access.AccessKey);
            token.ShouldNotBeNull();

            var validator = new AuthKitBearerTokenValidator(options, new AuthKitClaimNormalizer(options), new TestHostEnvironment());
            var result = await validator.ValidateAsync(token);

            result.ShouldNotBeNull("The M2M token was rejected; check its iss, aud and org_id.");
            result.IsMachine.ShouldBeTrue();
            result.MachineId.ShouldBe(access.Id);
            result.Claims.ShouldContain(claim => claim.Type == "scp" && claim.Value.Contains("Annotation.Read"));

            var newSecret = await service.ResetMachineAccessAsync(access, "integration", "authkit");
            newSecret.ShouldNotBeNullOrWhiteSpace();
            (await RequestTokenAsync(http, settings.AuthKitDomain, access.Id, newSecret)).ShouldNotBeNull();
            (await WaitUntilRejectedAsync(http, settings.AuthKitDomain, access.Id, access.AccessKey))
                .ShouldBeTrue("The secret from before the reset still works.");
        }
        finally
        {
            (await service.DeleteMachineAccessAsync(access, "integration", "authkit")).ShouldBeTrue();
        }
    }

    // Null when WorkOS refuses the credentials.
    private static async Task<string?> RequestTokenAsync(HttpClient http, string domain, string clientId, string clientSecret)
    {
        using var response = await http.PostAsync($"{domain}/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }));

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("access_token").GetString();
    }

    // Secret revocation may take a moment to apply everywhere.
    private static async Task<bool> WaitUntilRejectedAsync(HttpClient http, string domain, string clientId, string clientSecret)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await RequestTokenAsync(http, domain, clientId, clientSecret) is null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }
}
