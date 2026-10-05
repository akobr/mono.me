using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public class KeycloakMachineAccessServiceTests
{
    private const string ClientsPath = "/admin/realms/storyteller/clients";
    private const string InternalId = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e";

    private readonly KeycloakStub _keycloak = new();

    [Fact]
    public async Task Create_RegistersAConfidentialClientThatDescribesItsTokens()
    {
        _keycloak.WithAdminToken()
            .On(HttpMethod.Post, ClientsPath, HttpStatusCode.Created, location: new Uri($"https://keycloak.example{ClientsPath}/{InternalId}"))
            .On(HttpMethod.Get, $"{ClientsPath}/{InternalId}/client-secret", HttpStatusCode.OK, """{"type":"secret","value":"kc-s3cret"}""");

        var access = await CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scope = MachineAccessScope.AnnotationReadWrite,
            AnnotationKey = "sbt.payments",
        });

        access.Id.ShouldMatch("^42\\.sform\\.org1\\.proj1\\.[0-9a-f]{32}$");
        access.ObjectId.ShouldBe(InternalId);
        access.AccessKey.ShouldBe("kc-s3cret");
        access.Scope.ShouldBe(MachineAccessScope.AnnotationReadWrite);
        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        access.TokenEndpoint.ShouldBe("https://keycloak.example/realms/storyteller/protocol/openid-connect/token");

        _keycloak.Requests.Select(request => $"{request.Method} {request.Path}").ShouldBe(
            [
                "POST /realms/master/protocol/openid-connect/token",
                $"POST {ClientsPath}",
                $"GET {ClientsPath}/{InternalId}/client-secret",
            ]);
        _keycloak.Requests[0].Body.ShouldBe("client_id=admin-cli&client_secret=admin-secret&grant_type=client_credentials");
        _keycloak.Requests.Skip(1).ShouldAllBe(request => request.Authorization == "Bearer admin-token");

        using var client = JsonDocument.Parse(_keycloak.Requests[1].Body!);
        var root = client.RootElement;
        root.GetProperty("clientId").GetString().ShouldBe(access.Id);
        root.GetProperty("serviceAccountsEnabled").GetBoolean().ShouldBeTrue();
        root.GetProperty("publicClient").GetBoolean().ShouldBeFalse();
        root.GetProperty("standardFlowEnabled").GetBoolean().ShouldBeFalse();
        root.GetProperty("description").GetString().ShouldBe("organization=org1|project=proj1|scope=AnnotationReadWrite|annotation=sbt.payments");
        root.GetProperty("attributes").GetProperty("42.annotation").GetString().ShouldBe("sbt.payments");

        var mappers = root.GetProperty("protocolMappers").EnumerateArray().ToDictionary(mapper => mapper.GetProperty("protocolMapper").GetString()!);
        mappers["oidc-audience-mapper"].GetProperty("config").GetProperty("included.custom.audience").GetString().ShouldBe("storyteller-api");
        var scopeConfig = mappers["oidc-hardcoded-claim-mapper"].GetProperty("config");
        scopeConfig.GetProperty("claim.name").GetString().ShouldBe("storyteller_scope");
        scopeConfig.GetProperty("claim.value").GetString().ShouldBe("Annotation.Read Annotation.ReadWrite");
        scopeConfig.GetProperty("access.token.claim").GetString().ShouldBe("true");
    }

    [Fact]
    public async Task Create_SecretFails_DeletesTheClientAgain()
    {
        _keycloak.WithAdminToken()
            .On(HttpMethod.Post, ClientsPath, HttpStatusCode.Created, location: new Uri($"https://keycloak.example{ClientsPath}/{InternalId}"))
            .On(HttpMethod.Get, $"{ClientsPath}/{InternalId}/client-secret", HttpStatusCode.InternalServerError)
            .On(HttpMethod.Delete, $"{ClientsPath}/{InternalId}", HttpStatusCode.NoContent);

        await Should.ThrowAsync<HttpRequestException>(() => CreateService().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scope = MachineAccessScope.DefaultRead,
        }));

        _keycloak.Requests.Last().ShouldSatisfyAllConditions(
            request => request.Method.ShouldBe(HttpMethod.Delete),
            request => request.Path.ShouldBe($"{ClientsPath}/{InternalId}"));
    }

    [Fact]
    public async Task Reset_RegeneratesTheSecretOfTheStoredClient()
    {
        _keycloak.WithAdminToken()
            .On(HttpMethod.Post, $"{ClientsPath}/{InternalId}/client-secret", HttpStatusCode.OK, """{"type":"secret","value":"kc-new"}""");

        var secret = await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        secret.ShouldBe("kc-new");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Delete_RemovesTheStoredClient(HttpStatusCode status, bool expected)
    {
        _keycloak.WithAdminToken().On(HttpMethod.Delete, $"{ClientsPath}/{InternalId}", status);

        (await CreateService().DeleteMachineAccessAsync(Existing(), "org1", "proj1")).ShouldBe(expected);
    }

    [Fact]
    public async Task Delete_ServerError_Throws()
    {
        _keycloak.WithAdminToken().On(HttpMethod.Delete, $"{ClientsPath}/{InternalId}", HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<HttpRequestException>(() => CreateService().DeleteMachineAccessAsync(Existing(), "org1", "proj1"));
    }

    [Fact]
    public async Task AdminToken_WithUserCredentials_UsesThePasswordGrant()
    {
        _keycloak.WithAdminToken().On(HttpMethod.Delete, $"{ClientsPath}/{InternalId}", HttpStatusCode.NoContent);
        var service = CreateService(options => (options.AdminClientSecret, options.AdminUsername, options.AdminPassword) = (null, "admin", "p@ss"));

        await service.DeleteMachineAccessAsync(Existing(), "org1", "proj1");

        _keycloak.Requests[0].Body.ShouldBe("client_id=admin-cli&grant_type=password&username=admin&password=p%40ss");
    }

    private static MachineAccess Existing()
    {
        return new MachineAccess
        {
            Id = "42.sform.org1.proj1.abc",
            ObjectId = InternalId,
            AccessKey = "kc-***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    private KeycloakMachineAccessService CreateService(Action<KeycloakOptions>? configure = null)
    {
        var options = new KeycloakOptions
        {
            ServerUrl = "https://keycloak.example/",
            Realm = "storyteller",
            AdminClientSecret = "admin-secret",
            Audience = "storyteller-api",
        };
        configure?.Invoke(options);
        return new KeycloakMachineAccessService(_keycloak, Options.Create(options));
    }
}
