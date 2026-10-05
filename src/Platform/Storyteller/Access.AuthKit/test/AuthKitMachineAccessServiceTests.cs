using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitMachineAccessServiceTests
{
    private static readonly Dictionary<string, string> PermissionMap = new()
    {
        ["storyteller:annotation-read"] = "Annotation.Read",
        ["storyteller:annotation-write"] = "Annotation.ReadWrite",
        ["storyteller:configuration-read"] = "Configuration.Read",
        ["storyteller:configuration-write"] = "Configuration.ReadWrite",
        ["storyteller:default-read"] = "Default.Read",
        ["storyteller:default-write"] = "Default.ReadWrite",
        ["storyteller:secrets"] = "Configuration.Secrets",
        ["storyteller:reader"] = "Annotation.Read Configuration.Read",
        ["storyteller:mixed"] = "Annotation.Read Configuration.Secrets",
    };

    private readonly FakeWorkOsManagementClient _client = new();

    [Fact]
    public async Task Create_MakesAnM2MApplicationInTheMachineOrganizationWithItsSecret()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(MachineAccessScope.DefaultRead));

        _client.Calls.ShouldBe(["create-application", "create-secret:conn_app_01"]);
        var application = _client.CreatedApplication.ShouldNotBeNull();
        application.Name.ShouldMatch("^42\\.sform\\.org1\\.proj1\\.[0-9a-f]{32}$");
        application.Description.ShouldBe("organization=org1|project=proj1|scope=DefaultRead");
        application.OrganizationId.ShouldBe("org_machines");
        application.Scopes.ShouldBe(["storyteller:annotation-read", "storyteller:configuration-read", "storyteller:default-read", "storyteller:reader"]);
        application.ApplicationType.ShouldBe("m2m");

        access.Id.ShouldBe("client_m2m01");
        access.ObjectId.ShouldBe("conn_app_01");
        access.AccessKey.ShouldBe("plain-secret-1");
        access.Scope.ShouldBe(MachineAccessScope.DefaultRead);
        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
    }

    [Fact]
    public async Task Create_ReadWrite_IncludesTheWriteSlugsButNeverSecrets()
    {
        await CreateService().CreateMachineAccessAsync(Model(MachineAccessScope.DefaultReadWrite));

        _client.CreatedApplication!.Scopes.ShouldBe(
            [
                "storyteller:annotation-read",
                "storyteller:annotation-write",
                "storyteller:configuration-read",
                "storyteller:configuration-write",
                "storyteller:default-read",
                "storyteller:default-write",
                "storyteller:reader",
            ]);
    }

    [Fact]
    public async Task Create_WithAnnotation_RecordsItInTheDescription()
    {
        await CreateService().CreateMachineAccessAsync(Model(MachineAccessScope.AnnotationRead) with { AnnotationKey = "sbt.payments" });

        _client.CreatedApplication!.Description.ShouldBe("organization=org1|project=proj1|scope=AnnotationRead|annotation=sbt.payments");
        _client.CreatedApplication.Scopes.ShouldBe(["storyteller:annotation-read"]);
    }

    [Fact]
    public async Task Create_SecretFails_DeletesTheApplicationAgain()
    {
        _client.CreateSecretFailure = new WorkOsApiException(System.Net.HttpStatusCode.InternalServerError, "boom");

        await Should.ThrowAsync<WorkOsApiException>(() => CreateService().CreateMachineAccessAsync(Model(MachineAccessScope.DefaultRead)));

        _client.Calls.ShouldBe(["create-application", "create-secret:conn_app_01", "delete-application:conn_app_01"]);
    }

    [Fact]
    public async Task Create_NoPermissionForTheScope_FailsBeforeCallingWorkOs()
    {
        var service = CreateService(permissionMap: new Dictionary<string, string> { ["storyteller:secrets"] = "Configuration.Secrets" });

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(Model(MachineAccessScope.DefaultRead)));

        exception.Message.ShouldContain("PermissionMap");
        _client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_WithoutMachineOrganization_IsNotSupported()
    {
        var service = CreateService(organizationId: null);

        await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(Model(MachineAccessScope.DefaultRead)));

        _client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_MintsBeforeRevokingTheOldSecrets()
    {
        _client.Secrets.Add(new WorkOsClientSecret("secret_old1", null, "a", DateTimeOffset.UtcNow.AddDays(-2)));
        _client.Secrets.Add(new WorkOsClientSecret("secret_old2", null, "b", DateTimeOffset.UtcNow.AddDays(-1)));

        var secret = await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        secret.ShouldBe("plain-secret-1");
        _client.Calls.ShouldBe(
            [
                "list-secrets:conn_app_01",
                "create-secret:conn_app_01",
                "delete-secret:secret_old1",
                "delete-secret:secret_old2",
            ]);
        _client.Secrets.Select(remaining => remaining.Id).ShouldBe(["secret_new1"]);
    }

    [Fact]
    public async Task Reset_AtTheSecretLimit_FreesTheOldestSlotFirst()
    {
        for (var index = 0; index < AuthKitMachineAccessService.MaxClientSecrets; index++)
        {
            _client.Secrets.Add(new WorkOsClientSecret($"secret_old{index}", null, null, DateTimeOffset.UtcNow.AddDays(index - 10)));
        }

        await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        _client.Calls.Take(3).ShouldBe(["list-secrets:conn_app_01", "delete-secret:secret_old0", "create-secret:conn_app_01"]);
        _client.Secrets.Select(remaining => remaining.Id).ShouldBe(["secret_new1"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_RemovesTheApplicationByItsObjectId(bool existed)
    {
        _client.DeleteApplicationResult = existed;

        var deleted = await CreateService().DeleteMachineAccessAsync(Existing(), "org1", "proj1");

        deleted.ShouldBe(existed);
        _client.Calls.ShouldBe(["delete-application:conn_app_01"]);
    }

    private static MachineAccessCreate Model(MachineAccessScope scope)
    {
        return new MachineAccessCreate { Organization = "org1", Project = "proj1", Scope = scope };
    }

    private static MachineAccess Existing()
    {
        return new MachineAccess
        {
            Id = "client_m2m01",
            ObjectId = "conn_app_01",
            AccessKey = "pla***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    private AuthKitMachineAccessService CreateService(Dictionary<string, string>? permissionMap = null, string? organizationId = "org_machines")
    {
        var options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                ApiKey = "sk_test",
                AuthKitDomain = "https://example.authkit.app",
                MachineOrganizationId = organizationId,
                PermissionMap = permissionMap ?? PermissionMap,
            },
        });

        return new AuthKitMachineAccessService(_client, options, NullLogger<AuthKitMachineAccessService>.Instance);
    }
}
