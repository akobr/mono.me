using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

// The real Graph SDK against a stub handler, so the request sequence and bodies are what Graph gets.
public class AzureAdMachineAccessServiceTests
{
    private const string MachineSp = "2f4b1c5e-0d6a-4f3e-9b8a-1c2d3e4f5a6b";
    private const string ApiSp = "7a8b9c0d-1e2f-4a3b-8c4d-5e6f7a8b9c0d";

    private readonly GraphStubHandler _graph = new();

    // Each set is assigned as it is, one role per scope: no preset collapses it into a wider role.
    public static TheoryData<MachineScope[]> ScopeSets => new()
    {
        { [MachineScope.AnnotationRead] },
        { [MachineScope.AnnotationWrite] },
        { [MachineScope.AnnotationRead, MachineScope.AnnotationWrite] },
        { [MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.SecretsRead] },
        { [MachineScope.SecretsRead, MachineScope.SecretsWrite] },
        { [.. MachineScopes.All] },
    };

    [Fact]
    public async Task Create_RegistersTheAppWithASecretAndOneAppRolePerScope()
    {
        StubCreate();

        var access = await CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            AnnotationKey = "sbt.payments",
        });

        access.Id.ShouldBe("app-id-1");
        access.ObjectId.ShouldBe("obj-1");
        access.AccessKey.ShouldBe("s3cret");
        access.Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        access.TokenEndpoint.ShouldBe("https://login.microsoftonline.com/machine-tenant/oauth2/v2.0/token");
        access.TokenScope.ShouldBe("api://api-client/.default");

        // Every role is configured, but this machine asks for two of them. The service principal of the API is found
        // before anything is created.
        _graph.Requests.Select(request => $"{request.Method} {request.PathAndQuery.Split('?')[0]}").ShouldBe(
            [
                "GET /v1.0/servicePrincipals",
                "POST /v1.0/applications",
                "POST /v1.0/applications/obj-1/addPassword",
                "POST /v1.0/servicePrincipals",
                $"POST /v1.0/servicePrincipals/{MachineSp}/appRoleAssignments",
                $"POST /v1.0/servicePrincipals/{MachineSp}/appRoleAssignments",
            ]);

        using var application = Application();
        application.RootElement.GetProperty("displayName").GetString()!.ShouldMatch("^42\\.sform\\.org1\\.proj1\\.[0-9a-f]{32}$");
        application.RootElement.GetProperty("notes").GetString().ShouldBe("organization=org1|project=proj1|scopes=AnnotationRead,ConfigurationRead|annotation=sbt.payments");
        application.RootElement.GetProperty("requiredResourceAccess").GetArrayLength().ShouldBe(1);
        var resourceAccess = application.RootElement.GetProperty("requiredResourceAccess")[0];
        resourceAccess.GetProperty("resourceAppId").GetString().ShouldBe("api-client");
        resourceAccess.GetProperty("resourceAccess").EnumerateArray()
            .Select(role => (role.GetProperty("id").GetString(), role.GetProperty("type").GetString()))
            .ShouldBe([(RoleId(MachineScope.AnnotationRead), "Role"), (RoleId(MachineScope.ConfigurationRead), "Role")]);

        _graph.Requests[0].PathAndQuery.ShouldContain("$filter=appId eq 'api-client'");

        var assignments = Assignments();
        assignments.Select(assignment => (string?)assignment["appRoleId"]).ShouldBe([RoleId(MachineScope.AnnotationRead), RoleId(MachineScope.ConfigurationRead)]);
        assignments.ShouldAllBe(assignment => (string?)assignment["principalId"] == MachineSp && (string?)assignment["resourceId"] == ApiSp);
    }

    [Theory]
    [MemberData(nameof(ScopeSets))]
    public async Task Create_AssignsExactlyTheRequestedScopes(MachineScope[] scopes)
    {
        StubCreate();

        var access = await CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = scopes,
        });

        // What is stored is what is assigned: one role per scope, and no other.
        access.Scopes.ShouldBe(scopes);
        Assignments().Select(assignment => (string?)assignment["appRoleId"]).ShouldBe(scopes.Select(RoleId));

        using var application = Application();
        application.RootElement.GetProperty("notes").GetString().ShouldBe($"organization=org1|project=proj1|scopes={string.Join(',', scopes)}");
        application.RootElement.GetProperty("requiredResourceAccess")[0].GetProperty("resourceAccess").EnumerateArray()
            .Select(role => role.GetProperty("id").GetString())
            .ShouldBe(scopes.Select(RoleId));
    }

    [Fact]
    public async Task Create_AssignsTheRolesInEnumOrder()
    {
        StubCreate();

        var access = await CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.SchemaWrite, MachineScope.ConfigurationTransfer, MachineScope.ConfigurationWrite, MachineScope.ConfigurationRead, MachineScope.SchemaWrite],
        });

        MachineScope[] expected = [MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.ConfigurationTransfer, MachineScope.SchemaWrite];
        access.Scopes.ShouldBe(expected);
        Assignments().Select(assignment => (string?)assignment["appRoleId"]).ShouldBe(expected.Select(RoleId));

        using var application = Application();
        application.RootElement.GetProperty("notes").GetString().ShouldBe("organization=org1|project=proj1|scopes=ConfigurationRead,ConfigurationWrite,ConfigurationTransfer,SchemaWrite");
    }

    [Theory]
    [InlineData(MachineScope.AnnotationWrite, "Annotation.Write")]
    [InlineData(MachineScope.SecretsRead, "Secrets.Read")]
    [InlineData(MachineScope.SecretsWrite, "Secrets.Write")]
    [InlineData(MachineScope.KeyValueManage, "KeyValue.Manage")]
    public async Task Create_RoleNotConfigured_FailsBeforeCallingGraph(MachineScope missing, string scopeString)
    {
        StubCreate();
        var service = CreateService(roles => roles.Remove(missing.ToString("G")));

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead, missing],
        }));

        exception.Message.ShouldBe(
            $"Machines with the scope {scopeString} need the app role Auth:AppRoles:{missing:G}, which this deployment does not configure.");
        _graph.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_RoleThatIsNoId_FailsBeforeCallingGraph()
    {
        StubCreate();
        var service = CreateService(roles => roles["AnnotationWrite"] = "not-a-guid");

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.AnnotationWrite],
        }));

        exception.Message.ShouldBe(
            "Machines with the scope Annotation.Write need the app role Auth:AppRoles:AnnotationWrite, which this deployment does not configure.");
        _graph.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_OtherRolesMissing_DoNotMatter()
    {
        StubCreate();
        var service = CreateService(roles =>
        {
            roles.Clear();
            roles["AnnotationRead"] = RoleId(MachineScope.AnnotationRead);
        });

        var access = await service.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead],
        });

        access.Scopes.ShouldBe([MachineScope.AnnotationRead]);
        Assignments().Select(assignment => (string?)assignment["appRoleId"]).ShouldBe([RoleId(MachineScope.AnnotationRead)]);
    }

    [Theory]
    [InlineData(MachineScope.AnnotationRead, "AnnotationRead")]
    [InlineData(MachineScope.SecretsWrite, "SecretsWrite")]
    [InlineData(MachineScope.ConfigurationTransfer, "ConfigurationTransfer")]
    [InlineData(MachineScope.KeyValueManage, "KeyValueManage")]
    public void GetRoleKey_IsTheEnumName(MachineScope scope, string key)
    {
        AzureAdMachineAccessService.GetRoleKey(scope).ShouldBe(key);
    }

    // The roles are assigned on the service principal of the API. Without it in the machine tenant nothing is created,
    // so no machine is stored with scopes it was never given.
    [Theory]
    [InlineData("""{"value":[]}""")]
    [InlineData("""{"value":[{"appId":"api-client"}]}""")]
    public async Task Create_WithoutTheApiServicePrincipal_FailsBeforeAnyWrite(string servicePrincipals)
    {
        StubCreate();
        _graph.On(HttpMethod.Get, "/v1.0/servicePrincipals", HttpStatusCode.OK, servicePrincipals);

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead],
        }));

        exception.Message.ShouldBe("The service principal of the Storyteller API application api-client was not found in the machine tenant, so no app role can be assigned.");
        _graph.Requests.Select(request => $"{request.Method} {request.PathAndQuery.Split('?')[0]}").ShouldBe(["GET /v1.0/servicePrincipals"]);
    }

    // A service principal of the machine without an id gets no roles: the create fails instead of returning scopes that
    // were not assigned.
    [Fact]
    public async Task Create_MachineServicePrincipalWithoutAnId_FailsWithoutAssigningARole()
    {
        StubCreate();
        _graph.On(HttpMethod.Post, "/v1.0/servicePrincipals", HttpStatusCode.Created, """{"appId":"app-id-1"}""");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead],
        }));

        exception.GetType().ShouldBe(typeof(InvalidOperationException));
        exception.Message.ShouldBe("The service principal of the machine registration in Azure AD was not created, so its app roles cannot be assigned.");
        _graph.Requests.ShouldNotContain(request => request.PathAndQuery.EndsWith("/appRoleAssignments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_WithoutTheSecret_Fails()
    {
        StubCreate(secretBody: """{"keyId":"5c2f6c8e-3a1b-4c5d-9e8f-0a1b2c3d4e5f"}""");

        await Should.ThrowAsync<InvalidOperationException>(() => CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead],
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

    // A distinct, readable app role ID per scope: ...-000000000001 for AnnotationRead, and so on.
    private static string RoleId(MachineScope scope)
    {
        return $"00000000-0000-4000-8000-{(int)scope + 1:D12}";
    }

    private static MachineAccess Existing()
    {
        return new MachineAccess
        {
            Id = "app-id-1",
            ObjectId = "obj-1",
            AccessKey = "s3c***",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    // The body of the new application.
    private JsonDocument Application()
    {
        return JsonDocument.Parse(_graph.Requests.Single(request => request.Method == HttpMethod.Post && request.PathAndQuery == "/v1.0/applications").Body!);
    }

    private List<JsonNode> Assignments()
    {
        return _graph.Requests
            .Where(request => request.PathAndQuery == $"/v1.0/servicePrincipals/{MachineSp}/appRoleAssignments")
            .Select(request => JsonNode.Parse(request.Body!)!)
            .ToList();
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

    // Every scope has its role unless configureRoles takes it away; the keys are the enum names.
    private AzureAdMachineAccessService CreateService(Action<Dictionary<string, string>>? configureRoles = null)
    {
        var client = new GraphServiceClient(new HttpClient(_graph), new AnonymousAuthenticationProvider(), "https://graph.microsoft.com/v1.0");
        var roles = Enum.GetValues<MachineScope>().ToDictionary(scope => scope.ToString("G"), RoleId);
        configureRoles?.Invoke(roles);

        return new AzureAdMachineAccessService(
            client,
            Options.Create(new UserAuthenticationOptions
            {
                TenantId = "common",
                ClientId = "api-client",
                AppRoles = roles,
            }),
            Options.Create(new AzureAdMachineAccessOptions { TenantId = "machine-tenant" }));
    }
}
