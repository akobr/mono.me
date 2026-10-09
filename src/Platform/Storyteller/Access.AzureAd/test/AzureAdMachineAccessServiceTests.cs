using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

// The real Graph SDK against a stub handler, so the request sequence and bodies are what Graph gets.
public class AzureAdMachineAccessServiceTests
{
    private const string ReadRole = "008029ec-407f-48b2-8618-f38b8e03eebf";
    private const string ReadWriteRole = "6eb2cfb3-a936-4077-8a58-620896871d32";
    private const string MachineSp = "2f4b1c5e-0d6a-4f3e-9b8a-1c2d3e4f5a6b";
    private const string ApiSp = "7a8b9c0d-1e2f-4a3b-8c4d-5e6f7a8b9c0d";

    private readonly GraphStubHandler _graph = new();

    [Theory]
    [InlineData(MachineAccessScope.DefaultRead, ReadRole, MachineAccessScope.DefaultRead)]
    [InlineData(MachineAccessScope.AnnotationRead, ReadRole, MachineAccessScope.DefaultRead)]
    [InlineData(MachineAccessScope.ConfigurationReadWrite, ReadWriteRole, MachineAccessScope.DefaultReadWrite)]
    public async Task Create_RegistersTheAppWithASecretAndTheAppRole(MachineAccessScope scope, string roleId, MachineAccessScope grantedScope)
    {
        StubCreate();

        var access = await CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scope = scope,
            AnnotationKey = "sbt.payments",
        });

        access.Id.ShouldBe("app-id-1");
        access.ObjectId.ShouldBe("obj-1");
        access.AccessKey.ShouldBe("s3cret");
        access.Scope.ShouldBe(grantedScope);
        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        access.TokenEndpoint.ShouldBe("https://login.microsoftonline.com/machine-tenant/oauth2/v2.0/token");
        access.TokenScope.ShouldBe("api://api-client/.default");

        _graph.Requests.Select(request => $"{request.Method} {request.PathAndQuery.Split('?')[0]}").ShouldBe(
            [
                "POST /v1.0/applications",
                "POST /v1.0/applications/obj-1/addPassword",
                "POST /v1.0/servicePrincipals",
                "GET /v1.0/servicePrincipals",
                $"POST /v1.0/servicePrincipals/{MachineSp}/appRoleAssignments",
            ]);

        using var application = JsonDocument.Parse(_graph.Requests[0].Body!);
        application.RootElement.GetProperty("displayName").GetString()!.ShouldMatch("^42\\.sform\\.org1\\.proj1\\.[0-9a-f]{32}$");
        application.RootElement.GetProperty("notes").GetString().ShouldBe($"organization=org1|project=proj1|scope={scope:G}|annotation=sbt.payments");
        var resourceAccess = application.RootElement.GetProperty("requiredResourceAccess")[0];
        resourceAccess.GetProperty("resourceAppId").GetString().ShouldBe("api-client");
        resourceAccess.GetProperty("resourceAccess")[0].GetProperty("id").GetString().ShouldBe(roleId);

        _graph.Requests[3].PathAndQuery.ShouldContain("$filter=appId eq 'api-client'");

        using var assignment = JsonDocument.Parse(_graph.Requests[4].Body!);
        assignment.RootElement.GetProperty("principalId").GetString().ShouldBe(MachineSp);
        assignment.RootElement.GetProperty("resourceId").GetString().ShouldBe(ApiSp);
        assignment.RootElement.GetProperty("appRoleId").GetString().ShouldBe(roleId);
    }

    [Fact]
    public async Task Create_WithoutTheSecret_Fails()
    {
        StubCreate(secretBody: """{"keyId":"5c2f6c8e-3a1b-4c5d-9e8f-0a1b2c3d4e5f"}""");

        await Should.ThrowAsync<InvalidOperationException>(() => CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scope = MachineAccessScope.DefaultRead,
        }));
    }

    [Fact]
    public async Task Reset_RemovesTheGeneratedSecretsThenAddsOne()
    {
        _graph
            .On(HttpMethod.Get, "/v1.0/applications/obj-1", HttpStatusCode.OK, """
                {"id":"obj-1","appId":"app-id-1","passwordCredentials":[
                  {"keyId":"11111111-1111-1111-1111-111111111111","displayName":"Default secret, never expires. (42.sform.generated)"},
                  {"keyId":"22222222-2222-2222-2222-222222222222","displayName":"added by hand"}]}
                """)
            .On(HttpMethod.Post, "/v1.0/applications/obj-1/removePassword", HttpStatusCode.NoContent)
            .On(HttpMethod.Post, "/v1.0/applications/obj-1/addPassword", HttpStatusCode.OK, """{"secretText":"new-s3cret"}""");

        var secret = await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        secret.ShouldBe("new-s3cret");
        _graph.Requests.Select(request => $"{request.Method} {request.PathAndQuery}").ShouldBe(
            [
                "GET /v1.0/applications/obj-1",
                "POST /v1.0/applications/obj-1/removePassword",
                "POST /v1.0/applications/obj-1/addPassword",
            ]);
        _graph.Requests[1].Body!.ShouldContain("11111111-1111-1111-1111-111111111111");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Delete_RemovesTheApplicationByObjectId(HttpStatusCode status, bool expected)
    {
        _graph.On(HttpMethod.Delete, "/v1.0/applications/obj-1", status, status == HttpStatusCode.NotFound
            ? """{"error":{"code":"Request_ResourceNotFound","message":"Resource does not exist."}}"""
            : string.Empty);

        var deleted = await CreateService().DeleteMachineAccessAsync(Existing(), "org1", "proj1");

        deleted.ShouldBe(expected);
        _graph.Requests.Single().Method.ShouldBe(HttpMethod.Delete);
    }

    private static MachineAccess Existing()
    {
        return new MachineAccess
        {
            Id = "app-id-1",
            ObjectId = "obj-1",
            AccessKey = "s3c***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    private void StubCreate(string secretBody = """{"keyId":"5c2f6c8e-3a1b-4c5d-9e8f-0a1b2c3d4e5f","secretText":"s3cret"}""")
    {
        _graph
            .On(HttpMethod.Post, "/v1.0/applications", HttpStatusCode.Created, """{"id":"obj-1","appId":"app-id-1"}""")
            .On(HttpMethod.Post, "/v1.0/applications/obj-1/addPassword", HttpStatusCode.OK, secretBody)
            .On(HttpMethod.Post, "/v1.0/servicePrincipals", HttpStatusCode.Created, $$"""{"id":"{{MachineSp}}","appId":"app-id-1"}""")
            .On(HttpMethod.Get, "/v1.0/servicePrincipals", HttpStatusCode.OK, $$"""{"value":[{"id":"{{ApiSp}}","appId":"api-client"}]}""")
            .On(HttpMethod.Post, $"/v1.0/servicePrincipals/{MachineSp}/appRoleAssignments", HttpStatusCode.Created, """{"id":"assignment-1"}""");
    }

    private AzureAdMachineAccessService CreateService()
    {
        var client = new GraphServiceClient(new HttpClient(_graph), new AnonymousAuthenticationProvider(), "https://graph.microsoft.com/v1.0");

        return new AzureAdMachineAccessService(
            client,
            Options.Create(new UserAuthenticationOptions
            {
                TenantId = "common",
                ClientId = "api-client",
                AppRoles = new Dictionary<string, string>
                {
                    [AzureAdMachineAccessService.ReadRoleKey] = ReadRole,
                    [AzureAdMachineAccessService.ReadWriteRoleKey] = ReadWriteRole,
                },
            }),
            Options.Create(new AzureAdMachineAccessOptions { TenantId = "machine-tenant" }));
    }
}
